# ADR 0007 · Evaluación versionada con control negativo y compuerta determinista

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
Un asistente sin medición no se puede mejorar ni defender. La medición también puede engañar: si las métricas no
caen cuando se le quita el contexto, no miden el RAG.

## Decisión
- `eval/siniestros.jsonl`: 50 casos sintéticos con tipo, cláusula, datos faltantes, exclusiones y señales esperados,
  versionados ANTES del modelo extractivo y del prompt (historial de Git).
- `perito-eval` corre cada caso por la composición real de Application (alta, póliza, aviso, propuesta) en tres
  contextos: normal, **vacío** y **barajado** (lo recuperado para otro caso del mismo ramo con otra cláusula).
- Un espía alrededor del servicio de chat guarda todo lo enviado para contar datos personales en los prompts.
- Compuerta del CI (`eval/umbrales.json`) con la línea base determinista: clasificación ≥ 0.85, cita ≥ 0.85, datos
  faltantes F1 ≥ 0.80, recuperación ≥ 0.95, cero datos personales, y la cita debe caer al menos 0.50 con cada
  control. El informe versionado debe ser idéntico al que produce el commit (diff en el CI).
- Una corrida con Ollama se reporta aparte (`eval/informe/ollama-*.md`) y no es compuerta.

## Consecuencias
- Primera corrida de la línea base: clasificación 0.86, cita 0.86, datos F1 0.85. Un único ajuste después de verla
  (la negación se tomaba de otra frase en S05 y S27) llevó datos F1 a 0.86; está en el historial.
- El conjunto lo escribió la misma persona que el corpus y la línea base: sirve de compuerta de regresión, no de
  estimación de exactitud en producción (etapa 2: juego retenido escrito por otra persona).
