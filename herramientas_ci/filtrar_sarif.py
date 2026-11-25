"""Quita de un SARIF los resultados cuya ubicación cae en una ruta dada (p. ej. `/obj/`).

CodeQL de C# compilado también analiza el código que GENERA el compilador (el generador de expresiones regulares deja
sus fuentes en obj/): esas alertas no son de este repositorio y no se pueden corregir aquí. `paths-ignore` no aplica a
lenguajes compilados con build-mode manual, así que se filtran antes de subir el SARIF. El script dice cuántos quitó y
falla si el SARIF no trae ninguna corrida (un análisis vacío no es un análisis verde).

    python3 herramientas_ci/filtrar_sarif.py sarif-csharp/csharp.sarif /obj/
"""
from __future__ import annotations

import json
import sys
from pathlib import Path


def rutas(resultado: dict) -> list[str]:
    return [u.get('physicalLocation', {}).get('artifactLocation', {}).get('uri', '')
            for u in resultado.get('locations', [])]


def main(archivo: str, fragmento: str) -> int:
    ruta = Path(archivo)
    d = json.loads(ruta.read_text(encoding='utf-8'))
    corridas = d.get('runs', [])
    if not corridas:
        print(f'{archivo}: sin corridas → el análisis no ocurrió')
        return 1
    total = quitados = 0
    for corrida in corridas:
        resultados = corrida.get('results', [])
        total += len(resultados)
        vivos = [r for r in resultados if not any(fragmento in u for u in rutas(r))]
        quitados += len(resultados) - len(vivos)
        corrida['results'] = vivos
    ruta.write_text(json.dumps(d), encoding='utf-8')
    print(f'sarif: {total} resultados, {quitados} en «{fragmento}» (código generado) quitados, '
          f'{total - quitados} se suben')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1], sys.argv[2]))
