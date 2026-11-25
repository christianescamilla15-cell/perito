# Análisis de fallos · `openai/gpt-oss-120b` (Groq) frente a la línea base

Corrida del 2026-11-25 con los 50 casos sintéticos de `eval/siniestros.jsonl` (todo es ficticio). Cifras y detalle por
caso en [`informe/groq.md`](informe/groq.md); la línea base determinista en
[`informe/extractivo.md`](informe/extractivo.md). Este análisis lo escribió una persona leyendo esos informes.

## Resumen (contexto normal, N = 50)

- El LLM falla la **clasificación** en 7 casos, igual que la línea base (0.86), pero los fallos son de otra
  naturaleza: **ninguno es una confusión entre tipos**. Cinco son abstenciones (el modelo devuelve `null` cuando la
  cobertura no está contratada o una exclusión aplica) y dos son un nombre de tipo mal escrito.
- La línea base falla por vocabulario: confunde colisión con vuelco (S01, S06), fenómeno natural con robo (S12),
  cristales con robo (S26) y daños por agua con inundación (S34). El LLM acierta esos cinco.
- Ambos fallan S23 y S50, por razones distintas: la línea base se equivoca de tipo; el LLM se abstiene.
- **Cita:** 0.90 contra 0.86. En los dos casos del nombre mal escrito la cita sí era correcta (la validación descarta
  el tipo, no la cláusula).
- **Exclusiones a verificar:** el LLM encuentra muchas más (exhaustividad 0.82 contra 0.45) a costa de algunas de más
  (precisión 0.75 contra 1.00). Encuentra las que el texto dice con otras palabras y la línea base no ve: el sobrino
  «que todavía no tiene licencia» (S27, AUT-4.5), el taller de carpintería en la cochera (S30, HOG-4.5), «cuatro meses
  fuera del país» (S36, HOG-4.3) y «no hay huellas de que entraran a la fuerza» (S39, HOG-4.2). Se le escapan «había
  tomado unas copas» (S04, AUT-4.1) y la gasolina arrojada en una discusión (S49, HOG-4.4), y marca tres que no
  corresponden (S29, S41, S42).
- **Señales de riesgo:** 0.56/0.56 contra 1.00/1.00. Las expresiones de la línea base se escribieron sobre las frases
  del conjunto; el LLM las interpreta y a veces no las marca o marca otras.

## Fallos por tipo, con ejemplos

| Tipo | Caso | Qué dice el texto (ficticio) | Esperado | LLM | Por qué |
|---|---|---|---|---|---|
| Clasificación (abstención por exclusión) | S35 | humedad que «desde hace meses» fue creciendo | DanosPorAgua · HOG-2.2 · exclusión HOG-4.1 | `null` · `null` · exclusión HOG-4.1 | Marcó bien la exclusión y por eso no citó cobertura: leyó «si ninguna aplica, usa null» como «si no está cubierto» |
| Clasificación (abstención por exclusión) | S39 | faltan objetos, «no hay huellas de que entraran a la fuerza» | RoboEnVivienda · HOG-2.3 · exclusión HOG-4.2 | `null` · `null` · exclusión HOG-4.2 | El mismo patrón: la exclusión correcta lo llevó a no proponer tipo |
| Clasificación (abstención por carátula) | S25 | medallón roto, póliza sin Cristales | RoturaDeCristales · AUT-2.5 | `null` · `null` | Usó la función de la póliza para decidir cobertura: no contratada ⇒ nada. Lo contratado lo calcula la aplicación |
| Clasificación (abstención por carátula) | S50 | robo de una motocicleta, póliza básica sin robo | RoboEnVivienda · HOG-2.3 | `null` · `null` | Igual que S25 |
| Clasificación (nombre mal escrito) | S19, S20 | golpe al portón del vecino / a un taxi | DanosATercerosAuto · AUT-2.3 | «Dan**a**sATercerosAuto» · AUT-2.3 | Error de forma: la validación sólo acepta nombres exactos del catálogo, así que descarta el tipo y conserva la cita |
| Datos faltantes (de más) | S21 | «el encargado me dio sus datos y los de la dueña» | Lugar | Lugar, DatosDelTercero | No tomó como suficientes los datos que el tercero entregó |
| Datos faltantes (de menos) | S30 | incendio en un taller dentro de la casa | InventarioDeBienes | — | Omitió el inventario |

## Qué se concluye y qué no

- Con el mismo contexto, el LLM **entiende mejor** el texto que la línea base léxica (acierta las cinco paráfrasis que
  ella falla y encuentra casi el doble de exclusiones), pero la exactitud final empata porque **decide cuando sólo
  debía proponer**: se abstiene ante una exclusión o una cobertura no contratada. Es un defecto del prompt, no del
  modelo: la regla 1 («si ninguna aplica, usa null») es ambigua.
- Lo que sigue (etapa 2): aclarar en el prompt que el tipo y la cita describen el HECHO aunque no esté cubierto, y que
  contratación y exclusiones las decide la aplicación; medirlo con el mismo conjunto. El nombre mal escrito se puede
  corregir derivando el tipo de la cláusula citada cuando el tipo no es válido, siempre como propuesta.
- El conjunto lo escribió una sola persona y la corrida es una: las diferencias de un caso son 0.02 y no prueban
  nada por sí solas.
