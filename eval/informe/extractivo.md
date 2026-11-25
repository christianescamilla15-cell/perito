# Evaluación del asistente · extractivo (extractivo-v1)

Conjunto `eval/siniestros.jsonl` (huella `cd2b370fe53b2960`), 50 casos. Generado por `perito-eval`; no se edita a mano.

| Métrica | Normal | Vacio | Barajado |
|---|---:|---:|---:|
| Casos (N) | 50 | 50 | 50 |
| Clasificación (tipo exacto) | 0.86 | 0.00 | 0.34 |
| Cita correcta (cláusula exacta) | 0.86 | 0.00 | 0.34 |
| Cobertura correcta | 0.90 | 0.00 | 0.38 |
| Datos faltantes: precisión | 0.84 | 1.00 | 0.68 |
| Datos faltantes: exhaustividad | 0.89 | 0.00 | 0.46 |
| Datos faltantes: F1 | 0.86 | 0.00 | 0.55 |
| Datos faltantes: conjunto exacto | 0.90 | 0.36 | 0.52 |
| Exclusiones: precisión | 1.00 | 1.00 | 1.00 |
| Exclusiones: exhaustividad | 0.45 | 0.00 | 0.09 |
| Señales: precisión | 1.00 | 1.00 | 1.00 |
| Señales: exhaustividad | 1.00 | 1.00 | 1.00 |
| Recuperación de la cláusula esperada (top 4) | 0.98 | 0.00 | 0.42 |
| Recuperación de exclusiones esperadas (top 3) | 0.82 | 0.00 | 0.36 |
| Respuestas JSON legibles | 1.00 | 1.00 | 1.00 |
| Llamó a la función de la póliza | 1.00 | 1.00 | 1.00 |
| Datos personales en los prompts | 0 | 0 | 0 |
| Latencia media por propuesta (ms, con la espera del ritmo) | no aplica | no aplica | no aplica |
| Latencia p95 por propuesta (ms) | no aplica | no aplica | no aplica |
| Llamadas al modelo | 100 | 100 | 100 |
| Tokens de entrada | no aplica | no aplica | no aplica |
| Tokens de entrada servidos de la caché de prompts | no aplica | no aplica | no aplica |
| Tokens de salida (incluye razonamiento) | no aplica | no aplica | no aplica |
| Latencia media por llamada al modelo (ms) | no aplica | no aplica | no aplica |
| Latencia p95 por llamada al modelo (ms) | no aplica | no aplica | no aplica |

## Detalle (contexto normal)

| Caso | Esperado | Propuesto | Tipo | Cita | Datos faltantes | Exclusiones | Observaciones |
|---|---|---|:-:|:-:|---|---|---|
| S01 | Colision AUT-2.1-a | Vuelco AUT-2.1-b | ✗ | ✗ | PresupuestoDeReparacion |  |  |
| S02 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ |  |  |  |
| S03 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ | Fotografias, PresupuestoDeReparacion |  |  |
| S04 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S05 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ |  | AUT-4.3 |  |
| S06 | Colision AUT-2.1-a | Vuelco AUT-2.1-b | ✗ | ✗ |  | AUT-4.2 |  |
| S07 | Vuelco AUT-2.1-b | Vuelco AUT-2.1-b | ✓ | ✓ |  |  |  |
| S08 | Vuelco AUT-2.1-b | Vuelco AUT-2.1-b | ✓ | ✓ | Lugar, Fotografias, PresupuestoDeReparacion |  |  |
| S09 | Vuelco AUT-2.1-b | Vuelco AUT-2.1-b | ✓ | ✓ | PresupuestoDeReparacion | AUT-4.4 |  |
| S10 | FenomenoNaturalAuto AUT-2.1-c | FenomenoNaturalAuto AUT-2.1-c | ✓ | ✓ |  |  |  |
| S11 | FenomenoNaturalAuto AUT-2.1-c | FenomenoNaturalAuto AUT-2.1-c | ✓ | ✓ | Fotografias |  |  |
| S12 | FenomenoNaturalAuto AUT-2.1-c | RoboTotal AUT-2.2 | ✗ | ✗ | DenunciaAnteMinisterioPublico ✗ |  |  |
| S13 | IncendioDeVehiculo AUT-2.1-d | IncendioDeVehiculo AUT-2.1-d | ✓ | ✓ | DictamenDeBomberos |  |  |
| S14 | IncendioDeVehiculo AUT-2.1-d | IncendioDeVehiculo AUT-2.1-d | ✓ | ✓ |  |  |  |
| S15 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ |  |  |  |
| S16 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ | DenunciaAnteMinisterioPublico |  |  |
| S17 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ | Lugar, DenunciaAnteMinisterioPublico |  |  |
| S18 | RoboTotal AUT-2.2 | RoboTotal AUT-2.2 | ✓ | ✓ |  | AUT-4.2 |  |
| S19 | DanosATercerosAuto AUT-2.3 | DanosATercerosAuto AUT-2.3 | ✓ | ✓ |  |  |  |
| S20 | DanosATercerosAuto AUT-2.3 | DanosATercerosAuto AUT-2.3 | ✓ | ✓ | DatosDelTercero |  |  |
| S21 | DanosATercerosAuto AUT-2.3 | DanosATercerosAuto AUT-2.3 | ✓ | ✓ | Lugar |  |  |
| S22 | LesionesAOcupantes AUT-2.4 | LesionesAOcupantes AUT-2.4 | ✓ | ✓ |  |  |  |
| S23 | LesionesAOcupantes AUT-2.4 | Colision AUT-2.1-a | ✗ | ✗ | Fotografias, PresupuestoDeReparacion ✗ |  |  |
| S24 | RoturaDeCristales AUT-2.5 | RoturaDeCristales AUT-2.5 | ✓ | ✓ |  |  |  |
| S25 | RoturaDeCristales AUT-2.5 | RoturaDeCristales AUT-2.5 | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S26 | RoturaDeCristales AUT-2.5 | RoboTotal AUT-2.2 | ✗ | ✗ | Lugar, DenunciaAnteMinisterioPublico ✗ |  |  |
| S27 | Colision AUT-2.1-a | Colision AUT-2.1-a | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S28 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ |  |  |  |
| S29 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ | DictamenDeBomberos, InventarioDeBienes |  |  |
| S30 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ | InventarioDeBienes |  |  |
| S31 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ | DictamenDeBomberos, InventarioDeBienes |  |  |
| S32 | Explosion HOG-2.1-b | Explosion HOG-2.1-b | ✓ | ✓ |  |  |  |
| S33 | Explosion HOG-2.1-b | Explosion HOG-2.1-b | ✓ | ✓ | DictamenDeBomberos, InventarioDeBienes |  |  |
| S34 | DanosPorAgua HOG-2.2 | InundacionPorLluvia HOG-2.4-a | ✗ | ✗ | InventarioDeBienes ✗ |  |  |
| S35 | DanosPorAgua HOG-2.2 | DanosPorAgua HOG-2.2 | ✓ | ✓ | Fotografias, PresupuestoDeReparacion | HOG-4.1 |  |
| S36 | DanosPorAgua HOG-2.2 | DanosPorAgua HOG-2.2 | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S37 | DanosPorAgua HOG-2.2 | DanosPorAgua HOG-2.2 | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S38 | RoboEnVivienda HOG-2.3 | RoboEnVivienda HOG-2.3 | ✓ | ✓ |  |  |  |
| S39 | RoboEnVivienda HOG-2.3 | RoboEnVivienda HOG-2.3 | ✓ | ✓ | DenunciaAnteMinisterioPublico, InventarioDeBienes |  |  |
| S40 | RoboEnVivienda HOG-2.3 | RoboEnVivienda HOG-2.3 | ✓ | ✓ | InventarioDeBienes |  |  |
| S41 | RoboEnVivienda HOG-2.3 | RoboEnVivienda HOG-2.3 | ✓ | ✓ | DenunciaAnteMinisterioPublico, InventarioDeBienes |  |  |
| S42 | InundacionPorLluvia HOG-2.4-a | InundacionPorLluvia HOG-2.4-a | ✓ | ✓ |  |  |  |
| S43 | InundacionPorLluvia HOG-2.4-a | InundacionPorLluvia HOG-2.4-a | ✓ | ✓ | Fotografias, InventarioDeBienes |  |  |
| S44 | GranizoOViento HOG-2.4-b | GranizoOViento HOG-2.4-b | ✓ | ✓ |  |  |  |
| S45 | GranizoOViento HOG-2.4-b | GranizoOViento HOG-2.4-b | ✓ | ✓ | Fotografias, PresupuestoDeReparacion |  |  |
| S46 | DanosATercerosHogar HOG-2.5 | DanosATercerosHogar HOG-2.5 | ✓ | ✓ |  |  |  |
| S47 | DanosATercerosHogar HOG-2.5 | DanosATercerosHogar HOG-2.5 | ✓ | ✓ | PresupuestoDeReparacion, DatosDelTercero |  |  |
| S48 | DanosATercerosHogar HOG-2.5 | DanosATercerosHogar HOG-2.5 | ✓ | ✓ | PresupuestoDeReparacion |  |  |
| S49 | IncendioDeVivienda HOG-2.1-a | IncendioDeVivienda HOG-2.1-a | ✓ | ✓ | InventarioDeBienes |  |  |
| S50 | RoboEnVivienda HOG-2.3 | DanosATercerosHogar HOG-2.5 | ✗ | ✗ | PresupuestoDeReparacion, DatosDelTercero ✗ |  |  |
