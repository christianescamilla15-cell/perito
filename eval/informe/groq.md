# Evaluación del asistente · groq (openai/gpt-oss-120b)

Conjunto `eval/siniestros.jsonl` (huella `cd2b370fe53b2960`), 50 casos. Generado por `perito-eval`; no se edita a mano.

> **PARCIAL.** No todos los contextos completaron los casos; cada columna dice su N y la comparación usa la línea base sobre los MISMOS casos.

- Corrida: 2026-11-25 · temperature=0 · reasoning_effort=low · max_tokens=2048 · ritmo 28 RPM y 7800 TPM · reintentos con retry-after (tope 90 s)
- Mismo RAG, mismo prompt, misma función de la póliza y mismas validaciones que la línea base; sólo cambia el servicio de chat.

| Métrica | Normal | Vacio | Barajado |
|---|---:|---:|---:|
| Casos (N) | 50 | 40 | 38 |
| Clasificación (tipo exacto) | 0.86 | 0.00 | 0.45 |
| Cita correcta (cláusula exacta) | 0.90 | 0.00 | 0.42 |
| Cobertura correcta | 0.86 | 0.00 | 0.45 |
| Datos faltantes: precisión | 0.97 | 1.00 | 0.67 |
| Datos faltantes: exhaustividad | 0.83 | 0.03 | 0.63 |
| Datos faltantes: F1 | 0.89 | 0.06 | 0.65 |
| Datos faltantes: conjunto exacto | 0.86 | 0.40 | 0.63 |
| Exclusiones: precisión | 0.75 | 1.00 | 0.29 |
| Exclusiones: exhaustividad | 0.82 | 0.00 | 0.22 |
| Señales: precisión | 0.56 | 0.44 | 0.33 |
| Señales: exhaustividad | 0.56 | 0.67 | 0.50 |
| Recuperación de la cláusula esperada (top 4) | 0.98 | 0.00 | 0.45 |
| Recuperación de exclusiones esperadas (top 3) | 0.82 | 0.00 | 0.33 |
| Respuestas JSON legibles | 1.00 | 1.00 | 1.00 |
| Llamó a la función de la póliza | 1.00 | 1.00 | 1.00 |
| Datos personales en los prompts | 0 | 0 | 0 |
| Latencia media por propuesta (ms, con la espera del ritmo) | 16990 | 4606 | 12124 |
| Latencia p95 por propuesta (ms) | 51156 | 6557 | 45109 |
| Llamadas al modelo | 100 | 80 | 76 |
| Tokens de entrada | 115,076 | 63,016 | 89,974 |
| Tokens de entrada servidos de la caché de prompts | 34,048 | 38,400 | 37,632 |
| Tokens de salida (incluye razonamiento) | 14,089 | 6,611 | 10,108 |
| Latencia media por llamada al modelo (ms) | 720 | 1222 | 3448 |
| Latencia p95 por llamada al modelo (ms) | 1235 | 3115 | 9491 |

Costo a precio de lista (USD por millón: 0.15 de entrada, 0.075 de entrada en caché, 0.60 de salida): **USD 0.0504** por las corridas de este informe (en la capa gratuita no se cobra).

## openai/gpt-oss-120b contra la línea base determinista (extractivo-v1)

La línea base NO es un LLM: elige por coincidencia ponderada de palabras. El LLM recibe exactamente el mismo contexto.

| Métrica | LLM Normal | Base Normal | LLM Vacio | Base Vacio | LLM Barajado | Base Barajado |
|---|---:|---:|---:|---:|---:|---:|
| Casos (N) | 50 | 50 | 40 | 40 | 38 | 38 |
| Clasificación | 0.86 | 0.86 | 0.00 | 0.00 | 0.45 | 0.37 |
| Cita | 0.90 | 0.86 | 0.00 | 0.00 | 0.42 | 0.37 |
| Datos faltantes F1 | 0.89 | 0.86 | 0.06 | 0.00 | 0.65 | 0.60 |
| Exclusiones: exhaustividad | 0.82 | 0.45 | 0.00 | 0.00 | 0.22 | 0.11 |
| Exclusiones: precisión | 0.75 | 1.00 | 1.00 | 1.00 | 0.29 | 1.00 |
| Señales: exhaustividad | 0.56 | 1.00 | 0.67 | 1.00 | 0.50 | 1.00 |
| Respuestas JSON legibles | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Llamó a la función | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Datos personales en los prompts | 0 | 0 | 0 | 0 | 0 | 0 |

## Fallos del LLM por tipo (contexto normal)

- Clasificación (tipo distinto): 7 de 50
- Cita (tipo correcto, cláusula distinta o descartada): 0
- Datos faltantes (tipo correcto, conjunto distinto): 2

| Tipo de fallo | Caso | Esperado | Propuesto | Datos faltantes esperados → propuestos | Observaciones |
|---|---|---|---|---|---|
| clasificación | S19 | DanosATercerosAuto AUT-2.3 | — AUT-2.3 |  →  | tipo descartado: «DanasATercerosAuto» no es un tipo del ramo Auto |
| clasificación | S20 | DanosATercerosAuto AUT-2.3 | — AUT-2.3 | DatosDelTercero → DatosDelTercero | tipo descartado: «DanasATercerosAuto» no es un tipo del ramo Auto |
| clasificación | S23 | LesionesAOcupantes AUT-2.4 | — — | InformeMedico →  | el modelo no propuso tipo; el modelo no citó una cláusula |
| clasificación | S25 | RoturaDeCristales AUT-2.5 | — — | PresupuestoDeReparacion →  | tipo descartado: «null» no es un tipo del ramo Auto; cita descartada: null no está entre las cláusulas recuperadas |
| clasificación | S35 | DanosPorAgua HOG-2.2 | — — | Fotografias, PresupuestoDeReparacion →  | el modelo no propuso tipo; el modelo no citó una cláusula |
| datos faltantes | S21 | DanosATercerosAuto AUT-2.3 | DanosATercerosAuto AUT-2.3 | Lugar → Lugar, DatosDelTercero |  |
| datos faltantes | S30 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | InventarioDeBienes →  |  |

## Detalle (contexto normal)

| Caso | Esperado | Propuesto | Tipo | Cita | Datos faltantes | Exclusiones | Observaciones |
|---|---|---|:-:|:-:|---|---|---|
| S01 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S02 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ |  |  |  |
| S03 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ | Fotografias, PresupuestoDeReparacion |  |  |
| S04 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S05 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ |  | AUT-4.3 |  |
| S06 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ |  | AUT-4.2 |  |
| S07 | Vuelco AUT-2.1-b | Vuelco AUT-2.1-b | ✓ | ✓ |  |  |  |
| S08 | Vuelco AUT-2.1-b | Vuelco AUT-2.1-b | ✓ | ✓ | Lugar, Fotografias, PresupuestoDeReparacion |  |  |
| S09 | Vuelco AUT-2.1-b | Vuelco AUT-2.1-b | ✓ | ✓ | PresupuestoDeReparacion | AUT-4.4 |  |
| S10 | FenomenoNaturalAuto AUT-2.1-c | FenomenoNaturalAuto AUT-2.1-c | ✓ | ✓ |  |  |  |
| S11 | FenomenoNaturalAuto AUT-2.1-c | FenomenoNaturalAuto AUT-2.1-c | ✓ | ✓ | Fotografias |  |  |
| S12 | FenomenoNaturalAuto AUT-2.1-c | FenomenoNaturalAuto AUT-2.1-c | ✓ | ✓ | Fotografias |  |  |
| S13 | IncendioDeVehiculo AUT-2.1-d | IncendioDeVehiculo AUT-2.1-d | ✓ | ✓ | DictamenDeBomberos |  |  |
| S14 | IncendioDeVehiculo AUT-2.1-d | IncendioDeVehiculo AUT-2.1-d | ✓ | ✓ |  |  |  |
| S15 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ |  |  |  |
| S16 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ | DenunciaAnteMinisterioPublico |  |  |
| S17 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ | Lugar, DenunciaAnteMinisterioPublico |  |  |
| S18 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ |  | AUT-4.2 |  |
| S19 | DanosATercerosAuto AUT-2.3 | — AUT-2.3 | ✗ | ✓ |  |  | tipo descartado: «DanasATercerosAuto» no es un tipo del ramo Auto |
| S20 | DanosATercerosAuto AUT-2.3 | — AUT-2.3 | ✗ | ✓ | DatosDelTercero |  | tipo descartado: «DanasATercerosAuto» no es un tipo del ramo Auto |
| S21 | DanosATercerosAuto AUT-2.3 | DanosATercerosAuto AUT-2.3 | ✓ | ✓ | Lugar, DatosDelTercero ✗ |  |  |
| S22 | LesionesAOcupantes AUT-2.4 | LesionesAOcupantes AUT-2.4 | ✓ | ✓ |  |  |  |
| S23 | LesionesAOcupantes AUT-2.4 | — — | ✗ | ✗ |  ✗ |  | el modelo no propuso tipo; el modelo no citó una cláusula |
| S24 | RoturaDeCristales AUT-2.5 | RoturaDeCristales AUT-2.5 | ✓ | ✓ |  |  |  |
| S25 | RoturaDeCristales AUT-2.5 | — — | ✗ | ✗ |  ✗ |  | tipo descartado: «null» no es un tipo del ramo Auto; cita descartada: null no está entre las cláusulas recuperadas |
| S26 | RoturaDeCristales AUT-2.5 | RoturaDeCristales AUT-2.5 | ✓ | ✓ | Fotografias, PresupuestoDeReparacion |  |  |
| S27 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ | PresupuestoDeReparacion | AUT-4.5 |  |
| S28 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ |  |  |  |
| S29 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ | DictamenDeBomberos, InventarioDeBienes | HOG-4.5 |  |
| S30 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ |  ✗ | HOG-4.5 |  |
| S31 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ | DictamenDeBomberos, InventarioDeBienes |  |  |
| S32 | Explosion HOG-2.1-b | Explosion HOG-2.1-b | ✓ | ✓ |  |  |  |
| S33 | Explosion HOG-2.1-b | Explosion HOG-2.1-b | ✓ | ✓ | DictamenDeBomberos, InventarioDeBienes |  |  |
| S34 | DanosPorAgua HOG-2.2 | DanosPorAgua HOG-2.2 | ✓ | ✓ |  |  |  |
| S35 | DanosPorAgua HOG-2.2 | — — | ✗ | ✗ |  ✗ | HOG-4.1 | el modelo no propuso tipo; el modelo no citó una cláusula |
| S36 | DanosPorAgua HOG-2.2 | DanosPorAgua HOG-2.2 | ✓ | ✓ | PresupuestoDeReparacion | HOG-4.3 |  |
| S37 | DanosPorAgua HOG-2.2 | DanosPorAgua HOG-2.2 | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S38 | RoboEnVivienda HOG-2.3 | RoboEnVivienda HOG-2.3 | ✓ | ✓ |  |  |  |
| S39 | RoboEnVivienda HOG-2.3 | — — | ✗ | ✗ |  ✗ | HOG-4.2 | el modelo no propuso tipo; el modelo no citó una cláusula |
| S40 | RoboEnVivienda HOG-2.3 | RoboEnVivienda HOG-2.3 | ✓ | ✓ | InventarioDeBienes |  |  |
| S41 | RoboEnVivienda HOG-2.3 | RoboEnVivienda HOG-2.3 | ✓ | ✓ | DenunciaAnteMinisterioPublico, InventarioDeBienes | HOG-4.2 |  |
| S42 | InundacionPorLluvia HOG-2.4-a | InundacionPorLluvia HOG-2.4-a | ✓ | ✓ |  | HOG-4.4 |  |
| S43 | InundacionPorLluvia HOG-2.4-a | InundacionPorLluvia HOG-2.4-a | ✓ | ✓ | Fotografias, InventarioDeBienes |  |  |
| S44 | GranizoOViento HOG-2.4-b | GranizoOViento HOG-2.4-b | ✓ | ✓ |  |  |  |
| S45 | GranizoOViento HOG-2.4-b | GranizoOViento HOG-2.4-b | ✓ | ✓ | Fotografias, PresupuestoDeReparacion |  |  |
| S46 | DanosATercerosHogar HOG-2.5 | DanosATercerosHogar HOG-2.5 | ✓ | ✓ |  |  |  |
| S47 | DanosATercerosHogar HOG-2.5 | DanosATercerosHogar HOG-2.5 | ✓ | ✓ | PresupuestoDeReparacion, DatosDelTercero |  |  |
| S48 | DanosATercerosHogar HOG-2.5 | DanosATercerosHogar HOG-2.5 | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S49 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ | InventarioDeBienes |  |  |
| S50 | RoboEnVivienda HOG-2.3 | — — | ✗ | ✗ |  ✗ |  | el modelo no propuso tipo; el modelo no citó una cláusula |
