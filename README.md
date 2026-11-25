# Perito

[![ci](https://github.com/christianescamilla15-cell/perito/actions/workflows/ci.yml/badge.svg)](https://github.com/christianescamilla15-cell/perito/actions/workflows/ci.yml)
[![seguridad](https://github.com/christianescamilla15-cell/perito/actions/workflows/seguridad.yml/badge.svg)](https://github.com/christianescamilla15-cell/perito/actions/workflows/seguridad.yml)

Sistema de **siniestros** de una aseguradora **ficticia** (Aseguradora Ficticia del Valle) con un **asistente de
triaje en Semantic Kernel** que propone y nunca decide. .NET 10 en capas limpias, PostgreSQL con bandeja de salida
transaccional y consumidor idempotente, privacidad por diseño y una evaluación del asistente medida con control
negativo. Todo es sintético: aseguradora, productos, condiciones, personas y siniestros.

Etapa 1 de 4 ([`docs/PLAN.md`](docs/PLAN.md)).

## Qué prueba (con cifras de este commit)

| Afirmación | Evidencia |
|---|---|
| Cada regla del dominio tiene su prueba | 43 reglas en [`docs/reglas.md`](docs/reglas.md), cada una con `archivo::prueba` (una prueba verifica que existan y no estén apagadas); los 6 ejemplos de cálculo de la cláusula GEN-7 se leen del documento y son el oráculo |
| La máquina de estados no admite atajos | las 47 combinaciones estado × acción no permitidas fallan con `transicion_invalida` sin cambiar estado ni eventos |
| El asistente propone y no decide | 6 propuestas hostiles no cambian el siniestro (en memoria y en PostgreSQL: `xmin` y bandeja intactos); aceptar una propuesta falsa termina en el dictamen de las reglas; en el IL el asistente no crea comandos ni cambia agregados (inyectar uno hace fallar la prueba) |
| Eventos sin pérdida ni duplicados | estado y evento en la misma transacción; un mensaje entregado dos veces se aplica una vez; dos despachadores concurrentes no duplican y la proyección cuadra con Σ de reservas vigentes; reintentos hasta mensaje muerto |
| Sin datos personales donde no deben estar | sin texto claro en la base (AES-256-GCM); cero datos personales conocidos en los prompts de 50 casos × 3 contextos; anonimizar no deja rastro en ninguna tabla; los eventos no tienen campos de texto libre |
| El RAG importa (control negativo) | línea base: con contexto vacío la cita cae de 0.86 a 0.00 y con contexto barajado a 0.34; LLM real (Groq): de 0.90 a 0.00 y a 0.42 |

**290 pruebas** en 6 proyectos, todas en verde con advertencias como errores y candados de NuGet bloqueados:

| Proyecto | Pruebas | Qué cubre |
|---|---:|---|
| `Perito.Domain.Tests` | 152 | reglas, matriz de transiciones, catálogo contra las condiciones, la tabla de reglas contra las pruebas, privacidad y seudonimizador |
| `Perito.Application.Tests` | 32 | casos de uso con almacén en memoria (copias por sesión), propone y no decide, validador de propuestas, finalidad y anonimización |
| `Perito.Asistente.Tests` | 58 | corpus, BM25, prompt, lista blanca de funciones, rondas, lectura tolerante, línea base, Groq y Ollama contra su protocolo HTTP (nombres de función, 429, ritmo), la llave que nunca sale y la compuerta de la evaluación |
| `Perito.Infrastructure.Tests` | 23 | PostgreSQL 16 real por Testcontainers: ida y vuelta, cifrado en reposo, concurrencia, bandeja de salida, idempotencia, despachadores concurrentes |
| `Perito.Api.Tests` | 12 | API real sobre PostgreSQL: 401/403, flujo completo por HTTP, finalidad, contrato de errores, límite 429, trabajador → tablero, arranque sin secretos |
| `Perito.Architecture.Tests` | 13 | capas (NetArchTest) y garantías en el IL (Mono.Cecil): asistente sin comandos, seudonimizador único, reloj único |

## Evaluación del asistente

50 siniestros sintéticos versionados **antes** del modelo ([`eval/`](eval)), cada uno con tipo, cláusula, datos
faltantes, exclusiones y señales esperados. `perito-eval` corre cada caso por el mismo manejador de Application que
usa la API (seudonimización, RAG, función de la póliza, validación) en tres contextos.

### Qué mide la línea base determinista (la compuerta del CI)

`extractivo` **no es un LLM**: lee el mismo prompt y elige la cláusula por coincidencia ponderada de palabras. Mide la
tubería (recuperación, función de la póliza, validación, seudonimización) y es la compuerta de regresión del CI —
[informe completo](eval/informe/extractivo.md):

| Métrica | Normal | Contexto vacío | Contexto barajado |
|---|---:|---:|---:|
| Clasificación (tipo exacto) | 0.86 | 0.00 | 0.34 |
| Cita correcta (cláusula exacta) | 0.86 | 0.00 | 0.34 |
| Datos faltantes, F1 | 0.86 | 0.00 | 0.55 |
| Recuperación de la cláusula esperada (top 4) | 0.98 | 0.00 | 0.42 |
| Exclusiones: precisión / exhaustividad | 1.00 / 0.45 | — / 0.00 | 1.00 / 0.09 |
| Datos personales en los prompts | 0 | 0 | 0 |

### Qué mide un LLM real (Groq)

<!-- GROQ:INICIO -->
`openai/gpt-oss-120b` en Groq, corrida del 2026-11-25 — [informe](eval/informe/groq.md) ·
[análisis de fallos](eval/analisis-groq.md). Mismo RAG, prompt, función y validaciones que la línea base; sólo cambia
el servicio de chat. **PARCIAL:** el límite diario de tokens de la capa gratuita (200 mil) cortó los controles: el
contexto normal tiene los 50 casos, el vacío 40 y el barajado 38. Cada comparación usa la línea base sobre los MISMOS
casos.

| Contexto | N | Clasificación LLM / base | Cita LLM / base | Datos faltantes F1 LLM / base |
|---|---:|---:|---:|---:|
| Normal | 50 | 0.86 / 0.86 | 0.90 / 0.86 | 0.89 / 0.86 |
| Vacío (control) | 40 | 0.00 / 0.00 | 0.00 / 0.00 | 0.06 / 0.00 |
| Barajado (control) | 38 | 0.45 / 0.37 | 0.42 / 0.37 | 0.65 / 0.60 |

- **Control negativo:** sin contexto la cita del LLM cae de 0.90 a 0.00 y con el contexto de otro caso a 0.42, por
  debajo de la recuperación de ese contexto (0.45): no cita lo que no recibió.
- **Exclusiones a verificar:** exhaustividad 0.82 contra 0.45 de la línea base (precisión 0.75 contra 1.00).
- **Los fallos son de otra naturaleza:** la línea base confunde tipos por vocabulario; el LLM acierta esas paráfrasis
  pero se abstiene (devuelve `null`) cuando una exclusión aplica o la cobertura no está contratada (5 casos) y escribe
  mal un nombre de tipo (2): decide cuando sólo debía proponer, por una regla ambigua del prompt.
- **Uso:** 256 llamadas, 268,066 tokens de entrada (110,080 servidos de la caché de prompts) y 30,808 de salida; USD
  0.0504 a precio de lista (la capa gratuita no cobra). Latencia por llamada al modelo: 0.72 s de media y 1.24 s p95
  en el contexto normal; por propuesta, 17 s de media por el ritmo que respeta los límites.
- La corrida encontró y corrigió un defecto: con herramientas habilitadas en la respuesta final, gpt-oss «llamaba» a
  una herramienta `json` y Groq devolvía 400; ahora la ronda final va con `tool_choice: none` (con prueba).
<!-- GROQ:FIN -->

El CI **nunca** llama a un LLM: los proveedores `groq` (conector OpenAI de Semantic Kernel contra el API compatible de
Groq) y `ollama` se prueban contra servidores que hablan su protocolo HTTP, sin red ni llave
([ADR 0009](docs/adr/0009-groq-como-llm-real-de-la-evaluacion.md)). Esas pruebas encontraron y corrigieron un defecto
real: con OllamaSharp la función llegaba con otro nombre y la lista blanca la rechazaba.

Límites honestos: el conjunto, el corpus y la línea base los escribió la misma persona; la primera corrida dio datos
F1 0.85 y un solo ajuste (la negación se tomaba de otra frase) la llevó a 0.86 ([ADR 0007](docs/adr/0007-evaluacion-versionada-con-control-negativo.md)).
Es una compuerta de regresión, no una estimación de exactitud en producción.

## Arquitectura

C4 (contexto, contenedores y componentes del asistente) y la secuencia de la bandeja de salida en
[`docs/arquitectura/`](docs/arquitectura/README.md); decisiones en [`docs/adr/`](docs/adr):

- [0002](docs/adr/0002-capas-limpias-net10-y-dominio-ignorante-de-la-persistencia.md) capas limpias y dominio que no conoce la persistencia
- [0003](docs/adr/0003-bandeja-de-salida-transaccional-y-consumidor-idempotente.md) bandeja de salida transaccional y consumidor idempotente
- [0004](docs/adr/0004-asistente-semantic-kernel-propone-y-no-decide.md) asistente con Semantic Kernel: propone y no decide
- [0005](docs/adr/0005-rag-lexico-bm25-en-la-etapa-1.md) RAG léxico BM25 filtrado por ramo
- [0006](docs/adr/0006-privacidad-por-diseno.md) privacidad por diseño
- [0007](docs/adr/0007-evaluacion-versionada-con-control-negativo.md) evaluación con control negativo
- [0008](docs/adr/0008-jwt-simetrico-en-la-etapa-1.md) JWT simétrico en la etapa 1

```
Perito.Domain ← Perito.Application ← Perito.Infrastructure (EF Core + Npgsql) ←┐
                                   ← Perito.Asistente (Semantic Kernel)      ←┴ Perito.Api (Minimal API)
```

## Cómo ejecutar

Requisitos: .NET SDK 10 y Docker (sólo para las pruebas de integración y la API local).

```bash
dotnet restore Perito.slnx --locked-mode
dotnet build Perito.slnx -c Release --no-restore
dotnet test tests/Perito.Domain.Tests -c Release --no-build           # y Application, Asistente, Architecture
dotnet test tests/Perito.Infrastructure.Tests -c Release --no-build   # levanta PostgreSQL con Testcontainers
dotnet run --project src/Perito.Evaluacion -c Release -- --compuerta eval/umbrales.json
```

La API en local, la prueba de humo, la evaluación con Ollama y la rotación de llaves están en
[`RUNBOOK.md`](RUNBOOK.md).

## Seguridad y privacidad

CodeQL (C# compilado), Semgrep, Trivy, gitleaks, SBOM y revisión de confidencialidad en cada cambio
([`SECURITY.md`](SECURITY.md)). Secretos sólo por variables de entorno (sin ellos la API no arranca). Cifrado de
campo AES-256-GCM, finalidad declarada y registro de accesos, anonimización a solicitud y seudonimización
obligatoria antes de cualquier prompt, al nivel de principios de leyes tipo LFPDPPP/GDPR sin pretender certificar
cumplimiento ([ADR 0006](docs/adr/0006-privacidad-por-diseno.md)).

## Lo que no está hecho (todavía)

- **Sin despliegue:** no hay imagen de contenedor ni ambiente; tampoco Trivy de imagen (no hay imagen). Etapa 3.
- **Sin OpenAPI publicado** ni identidad OIDC: JWT con llave simétrica sólo para local y pruebas (ADR 0008).
- **RAG léxico:** sin embeddings ni reordenamiento; la recuperación densa y el juego de evaluación retenido escrito por
  otra persona son la etapa 2.
- **Seudonimización por patrones:** un nombre de tercero sin señal («ayer Pedro me chocó») no se detecta.
- **Rotación de llaves sin re-cifrado en lote** y mensajes muertos sin panel (se atienden con SQL del runbook).
- **Otros proveedores de LLM:** ni Azure OpenAI ni otro están incluidos ni probados; sólo la línea base, Ollama y Groq.
- **Corrida con el LLM real incompleta en los controles** (vacío 40 de 50, barajado 38 de 50: límite diario de la capa
  gratuita) y una sola corrida: no hay intervalos ni repeticiones.
- Dominio acotado: sin pagos parciales, salvamento, subrogación ni reapertura (etapa 4).

## Licencia

MIT. Ver [`LICENSE`](LICENSE).
