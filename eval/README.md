# Conjunto de evaluación del asistente de triaje

50 siniestros **sintéticos** (27 de auto y 23 de hogar) con su clasificación, su cláusula, sus datos faltantes, las
exclusiones a verificar y las señales de riesgo esperadas. Las personas, teléfonos, correos, RFC, direcciones y
folios son ficticios (los correos usan los dominios reservados `example.*`).

- `siniestros.jsonl`: un caso por línea. `pii` lista los datos personales que aparecen en la descripción: una prueba
  exige que ninguno llegue a un prompt.
- `polizas.json`: las cuatro carátulas que usan los casos (auto amplia y limitada, hogar completa y básica).
- `umbrales.json`: la compuerta del CI (modelo extractivo) y lo que debe caer con el control negativo.
- `informe/`: el resultado versionado de cada corrida (`perito-eval`). `extractivo.*` es la línea base determinista y
  la compuerta del CI; `groq.*` es la corrida con un LLM real (fecha, modelo, tokens, costo, latencia y la comparación
  contra la línea base), que nunca corre en el CI ni es compuerta.
- `analisis-groq.md`: análisis escrito de los fallos del LLM real frente a la línea base, por tipo y con ejemplos.
- `corridas/` (fuera de Git): salidas locales y la caché de casos que permite reanudar una corrida cortada por un límite.

## Cómo se etiquetó

El conjunto se escribió y se versionó **antes** del modelo extractivo y del prompt (ver el historial de Git). Lo
etiquetó la misma persona que escribió las condiciones generales, con estas reglas:

| Campo | Regla |
|---|---|
| `tipo` | el daño principal que describe el texto. Si sólo se daña a otra persona o sus bienes, es responsabilidad civil; un intento de robo que sólo rompe un cristal es rotura de cristales; el rayo es incendio (HOG-2.1-a), no fenómeno hidrometeorológico. |
| `cobertura` y `clausula` | la cobertura que ampararía ese tipo según las condiciones, **aunque la carátula no la tenga**: lo contratado lo calcula la aplicación con la póliza, no el asistente. La cláusula es el inciso de la cobertura. |
| `datos_faltantes` | los `datos` que la cláusula pide para el tipo menos los que el texto dice que YA se tienen («tengo fotos», «ya levanté la denuncia», «me dieron presupuesto»). Que lleguen los bomberos no es su dictamen; nombrar dos objetos no es un inventario; «afuera de un bar» no es un lugar identificable; una negación («todavía no he ido al Ministerio Público») cuenta como faltante. |
| `exclusiones` | las cláusulas de exclusión que el texto obliga a verificar (alcohol, uso por plataforma o reparto, desgaste, arrancones, sin licencia, humedad de meses, sin huellas de violencia, vivienda sola más de 90 días, daño intencional, taller no declarado). Son a verificar: las confirma el ajustador. |
| `senales` | señales de riesgo en el texto: `SinTestigosNiEvidencia`, `BienesSinComprobante`, `RelatoContradictorio`, `ContratacionReciente`, `PresionPorPagoRapido`. No deciden nada: el riesgo que cuenta lo calcula el dominio con datos estructurados (GEN-9). |

## Límites honestos

- Es un conjunto pequeño y escrito por una sola persona: mide la tubería y sirve de compuerta de regresión; no es una
  estimación de la exactitud en producción.
- El modelo extractivo se escribió después del conjunto, pero por la misma persona: sus cifras son una línea base,
  no la calidad del asistente. La comparación que importa es la de un LLM contra esa línea base con los mismos casos.
- La etapa 2 agrega un juego retenido escrito por otra persona.
