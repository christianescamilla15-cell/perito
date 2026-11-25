# Plan · Perito (siniestros de una aseguradora ficticia con asistente de triaje)

Perito es un sistema de **siniestros** de la *Aseguradora Ficticia del Valle* (AFV): toma el aviso de un
siniestro, lo tría, lo asigna, reserva, dictamina y paga o rechaza, con las reglas de unas condiciones generales
escritas para el proyecto. Un asistente en Semantic Kernel lee la descripción libre y **propone** tipo, cobertura con
cita a la cláusula, datos faltantes y señales de riesgo; **nunca decide**: el dictamen sale de reglas deterministas.

Todo es ficticio: la aseguradora, los productos, las condiciones, las personas y los siniestros. No se citan leyes
ni artículos.

## 1. Dominio

### Flujo y estados

```
póliza ──► aviso (FNOL) ──► triaje ──► asignación ──► reserva ──► dictamen ──► pago
                                                         │            └──────► rechazo
                                     (sin elegibilidad) ─┴─► dictamen improcedente ──► rechazo
```

| Estado | Llega con | Sale con |
|---|---|---|
| `Avisado` | aviso de siniestro (FNOL) | triaje |
| `Triado` | triaje (tipo, cobertura, elegibilidad y riesgo calculados por reglas) | asignación |
| `Asignado` | asignación de ajustador (especialidad, carga, investigador si hay riesgo) | reserva o, si no es elegible, dictamen |
| `Reservado` | reserva constituida (y sus ajustes) | dictamen |
| `Dictaminado` | dictamen procedente o improcedente | pago (procedente) o rechazo (improcedente) |
| `Pagado` / `Rechazado` | pago con referencia / rechazo comunicado con su cláusula | — (finales) |

Cualquier otra transición es un error con nombre (se prueba una por una).

### Reglas (cada una con su prueba en `docs/reglas.md`)

| Regla | Qué decide | Cláusula |
|---|---|---|
| Vigencia | la ocurrencia cae entre inicio y fin, sin cancelación previa | GEN-2 |
| Prima y periodo de gracia | prima pagada hasta + 30 días naturales | GEN-3 |
| Plazo de aviso | 5 días naturales; el aviso tardío no anula, pero es señal de riesgo | GEN-4 |
| Suma asegurada | tope por evento de cada cobertura | GEN-5 |
| Deducible | fijo, % de la suma o % de la pérdida | GEN-6 |
| Coaseguro y cálculo | (pérdida − deducible) × (1 − coaseguro), tope en la suma; ejemplos de la cláusula como oráculo | GEN-7 |
| Documentación | no se dictamina sin los datos que pide el tipo de siniestro | GEN-8 y cada cobertura |
| Fraude básico por reglas | siniestro temprano, aviso tardío, frecuencia, monto cercano a la suma; dos o más ⇒ investigación | GEN-9 |
| Exclusiones | las confirma el ajustador; sólo cuentan las que aplican a la cobertura | AUT-4.x, HOG-4.x |
| Plazo de pago | 30 días naturales desde el dictamen; fuera de plazo se marca | GEN-10 |
| Rechazo | por escrito, con la causa y la cláusula | GEN-11 |
| Separación de funciones | quien dictamina no paga | interna |
| Decisiones automatizadas | ninguna propuesta del asistente cambia un estado | GEN-12 |

## 2. Etapas

### Etapa 1 (este corte): núcleo, eventos, privacidad y asistente medido

- TO-BE .NET 10 en capas (Domain, Application, Infrastructure con EF Core + Npgsql, API mínima REST con JWT y roles).
- Eventos de dominio con **outbox transaccional** y un **consumidor idempotente** (bandeja de entrada) que mantiene
  el tablero de reservas por ramo; reintentos con espera creciente y mensajes muertos.
- Reglas del dominio con su prueba por regla; pruebas de arquitectura (NetArchTest + IL con Mono.Cecil);
  integración con Testcontainers contra PostgreSQL real.
- Asistente de triaje con Semantic Kernel: RAG (BM25 filtrado por ramo) sobre las condiciones generales, llamada a
  función acotada al siniestro para consultar la póliza vía Application, salida JSON validada (cita sustentada en el
  contexto recuperado), proveedor intercambiable (modelo extractivo determinista en pruebas y CI; Ollama en local).
- Evaluación versionada (50 siniestros sintéticos) con control negativo (contexto vacío y barajado) y compuerta en CI.
- Privacidad: minimización, cifrado de campo AES-256-GCM, registro de accesos con finalidad, anonimización a
  solicitud y seudonimización obligatoria antes de cualquier prompt.

### Etapa 2: RAG denso y agentes

- Embeddings (Ollama, modelo chico) en pgvector; recuperación híbrida BM25 + densa con reordenamiento; medir recall@k
  contra la etapa 1 con el mismo conjunto.
- Segundo consumidor: propuesta de triaje asíncrona al recibir `SiniestroAvisado`.
- Agente de documentación: redacta la solicitud de datos faltantes al asegurado (borrador que un humano aprueba).
- Ampliar el conjunto de evaluación con un juego retenido escrito por otra persona.

### Etapa 3: operación

- Imagen de contenedor de la API (construida y arrancada en el CI, escaneada con Trivy), despliegue por pipeline con
  prueba de humo y reversa ensayada; OpenAPI publicado con su verificación.
- Identidad con un proveedor OIDC (hoy: JWT con llave simétrica configurada); rotación de llaves de cifrado con
  re-cifrado en lote.
- Observabilidad (OpenTelemetry) y tablero de latencia/costo del asistente.

### Etapa 4: dominio ampliado

- Recuperaciones (salvamento, subrogación), pagos parciales y reapertura; gastos médicos mayores como ramo propio.

## 3. Criterios de salida de la etapa 1 (medibles)

| Criterio | Cómo se mide | Meta |
|---|---|---|
| Compilación | `dotnet build -c Release` con advertencias como errores | 0 advertencias |
| Candados | `dotnet restore --locked-mode` | sin cambios |
| Reglas | cada regla de `docs/reglas.md` con al menos una prueba que la nombra | 100 % de las reglas |
| Transiciones inválidas | prueba por cada transición no permitida | todas |
| Integración | Testcontainers con PostgreSQL 16 real: persistencia, outbox atómico, consumidor idempotente, concurrencia, cifrado en reposo | en verde |
| Arquitectura | NetArchTest + IL: capas, el asistente no llama comandos ni métodos que cambian estado, sólo funciones de lectura | en verde |
| Evaluación (modelo extractivo) | `perito-eval` sobre 50 casos | clasificación ≥ 0.85 · cita ≥ 0.85 · datos faltantes F1 ≥ 0.80 · recall@6 ≥ 0.95 |
| Control negativo | contexto vacío y barajado | la cita cae al menos 50 puntos en los dos |
| Propone y no decide | prueba que ninguna propuesta cambia un estado sin las reglas | en verde |
| Privacidad | ningún prompt contiene un dato personal conocido de los 50 casos; columnas cifradas sin texto claro en la base | en verde |
| Seguridad | Semgrep `p/default` sin hallazgos, gitleaks, Trivy fs/config, confidencialidad con 0 hallazgos | en verde |

## 4. Qué se recorta si aprieta (en este orden)

1. La corrida con Ollama (se deja el conector probado por compilación y el comando para correrla; se reporta que no
   corrió).
2. El tablero de reservas por HTTP (el consumidor y su prueba de idempotencia se quedan).
3. Ajustes de reserva por HTTP (la regla y su prueba de dominio se quedan).
4. Las banderas de texto del asistente como métrica (se quedan en la propuesta, sin compuerta).

Nunca se recortan: las pruebas por regla, la integración contra PostgreSQL real, el control negativo de la
evaluación, la prueba de «propone y no decide», la seudonimización antes del prompt ni los escaneos de seguridad.
