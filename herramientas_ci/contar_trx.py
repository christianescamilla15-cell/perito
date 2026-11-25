"""Prueba de que `dotnet test` corrió algo: lee los contadores de cada TRX sin parser de XML (como
`herramientas/verificar_reportes.py` con JUnit) y falla si un proyecto ejecutó 0 pruebas o alguna falló."""
from __future__ import annotations

import re
import sys
from pathlib import Path


def contar(ruta: Path) -> tuple[int, int]:
    texto = ruta.read_text(encoding='utf-8', errors='replace')
    contadores = re.search(r'<Counters\b[^>]*?\bexecuted="(\d+)"[^>]*?\bfailed="(\d+)"', texto)
    if not contadores:
        raise SystemExit(f'{ruta}: sin contadores')
    return int(contadores.group(1)), int(contadores.group(2))


def main(rutas: list[str]) -> int:
    malo = False
    for ruta in rutas:
        ejecutadas, fallidas = contar(Path(ruta))
        print(f'{ruta}: {ejecutadas} pruebas ejecutadas, {fallidas} fallidas')
        malo |= ejecutadas == 0 or fallidas > 0
    return 1 if malo else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
