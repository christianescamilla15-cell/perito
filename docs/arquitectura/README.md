# Arquitectura · Perito

Modelo C4 en tres niveles (contexto, contenedores y componentes del asistente) y la secuencia de la bandeja de
salida. Los diagramas son texto (Mermaid) y describen lo que hay en el código de la etapa 1, no un plan. Las
decisiones están en [`docs/adr/`](../adr) y las reglas con su prueba en [`docs/reglas.md`](../reglas.md).

## 1. Contexto

```mermaid
C4Context
  title Contexto · Perito (siniestros de la Aseguradora Ficticia del Valle)
  Person(asegurado, "Asegurado", "Avisa el siniestro por un canal de atención")
  Person(operador, "Operador de atención", "Registra el aviso (FNOL)")
  Person(ajustador, "Ajustador", "Tría, reserva, recaba datos y dictamina")
  Person(supervisor, "Supervisor", "Asigna, comunica rechazos, ve el tablero")
  Person(tesoreria, "Tesorería", "Paga lo dictaminado procedente")
  Person(privacidad, "Oficial de privacidad", "Atiende derechos del titular")
  System(perito, "Perito", ".NET 10 + PostgreSQL: flujo del siniestro con reglas deterministas y asistente de triaje")
  System_Ext(llm, "Modelo de lenguaje (opcional)", "Ollama local; en pruebas y CI, la línea base extractiva")
  Rel(asegurado, operador, "Describe lo ocurrido")
  Rel(operador, perito, "Aviso", "HTTPS/JSON + JWT")
  Rel(ajustador, perito, "Propuesta, triaje, reserva, dictamen", "HTTPS/JSON + JWT")
  Rel(supervisor, perito, "Asignación, rechazo, tablero", "HTTPS/JSON + JWT")
  Rel(tesoreria, perito, "Pago", "HTTPS/JSON + JWT")
  Rel(privacidad, perito, "Accesos y anonimización", "HTTPS/JSON + JWT")
  Rel(perito, llm, "Texto seudonimizado y cláusulas", "HTTP (Ollama)")
```

## 2. Contenedores

```mermaid
C4Container
  title Contenedores · Perito (etapa 1)
  Person(usuarios, "Personas por rol", "operador, ajustador, supervisor, tesorería, suscriptor, privacidad")
  System_Boundary(p, "Perito") {
    Container(api, "API", "Perito.Api · Minimal API .NET 10", "JWT con roles, finalidad de lectura, límite de propuestas, contrato problem+json")
    Container(trabajador, "Trabajador de la bandeja", "BackgroundService en la API", "Despacha mensajes_de_salida con SKIP LOCKED, reintentos y muertos")
    Container(asistente, "Asistente de triaje", "Perito.Asistente · Semantic Kernel", "RAG BM25, función de lectura de la póliza, salida JSON")
    ContainerDb(pg, "PostgreSQL 16", "EF Core 10 + Npgsql", "Esquema perito: relacional + jsonb (propuestas, eventos); campos cifrados AES-GCM")
    Container(eval, "perito-eval", "Perito.Evaluacion · consola", "50 casos × 3 contextos, informe y compuerta")
  }
  System_Ext(ollama, "Ollama", "Modelo chico local (opcional)")
  Rel(usuarios, api, "JSON/HTTPS")
  Rel(api, pg, "Unidad de trabajo: estado + eventos en una transacción")
  Rel(api, asistente, "Propuesta (texto seudonimizado)", "en proceso")
  Rel(asistente, ollama, "Chat con llamada a funciones", "HTTP")
  Rel(trabajador, pg, "Lee la bandeja, escribe la proyección y la bandeja de entrada")
  Rel(eval, asistente, "Mismo manejador de Application, almacén en memoria")
```

## 3. Componentes del asistente

```mermaid
C4Component
  title Componentes · propuesta de triaje
  Container_Boundary(app, "Perito.Application") {
    Component(manejador, "ManejadoresDeTriaje", "IManejador<ProponerTriaje>", "Carga siniestro, póliza y titular; seudonimiza; guarda la propuesta validada")
    Component(seud, "Seudonimizador", "Domain.Privacidad", "Única fábrica de TextoSeudonimizado")
    Component(validador, "ValidadorDePropuesta", "estático", "Catálogo del ramo, cita sustentada, cobertura y contratación calculadas")
    Component(consulta, "ConsultarCoberturasDelSiniestro", "IConsulta", "Carátula sin datos personales")
  }
  Container_Boundary(sk, "Perito.Asistente") {
    Component(orq, "AsistenteDeTriaje", "IAsistenteDeTriaje", "Bucle de funciones con lista blanca, rondas y tiempo máximos")
    Component(rag, "RecuperadorBm25", "IRecuperadorDeClausulas", "Filtro por ramo; 4 coberturas + 3 exclusiones")
    Component(corpus, "LectorDeCondiciones", "recurso incrustado", "docs/condiciones/condiciones-generales.md")
    Component(funcion, "FuncionesDePoliza", "[KernelFunction]", "coberturas_contratadas(), acotada al siniestro")
    Component(chat, "IChatCompletionService", "SK", "ModeloExtractivo (CI) u OllamaSharp como IChatClient")
  }
  Rel(manejador, seud, "Texto + identificadores del titular")
  Rel(manejador, orq, "SolicitudAlAsistente")
  Rel(orq, rag, "Busca")
  Rel(rag, corpus, "Indexa")
  Rel(orq, chat, "Historia + ajustes (autoInvoke: false)")
  Rel(orq, funcion, "Sólo si está en la lista blanca")
  Rel(funcion, consulta, "IMediador")
  Rel(manejador, validador, "PropuestaGenerada")
```

## 4. Capas (las hacen cumplir `tests/Perito.Architecture.Tests`)

| Proyecto | Depende de | Contiene |
|---|---|---|
| `Perito.Domain` | sólo la BCL | agregados, máquina de estados, cálculo, catálogo del producto, eventos, `IClock`, seudonimizador |
| `Perito.Application` | Domain | casos de uso, puertos, mediador propio, `IComando`/`IConsulta`, validador de propuestas |
| `Perito.Infrastructure` | Application | EF Core + Npgsql, almacén y unidad de trabajo, bandeja de salida y de entrada, cifrado, registro de accesos, `SystemClock` |
| `Perito.Asistente` | Application | Semantic Kernel, RAG, función de la póliza, proveedores |
| `Perito.Api` | Infrastructure, Asistente | composición, rutas, JWT, trabajador |
| `Perito.EnMemoria`, `Perito.Evaluacion` | Application, Asistente | adaptadores en memoria y la evaluación |

## 5. Bandeja de salida (ADR 0003)

```mermaid
sequenceDiagram
  participant API as API (manejador)
  participant PG as PostgreSQL
  participant D as Despachador
  participant P as ProyeccionDeReservas
  API->>PG: BEGIN · UPDATE siniestros · INSERT mensajes_de_salida · COMMIT
  loop cada intervalo o mientras haya lote
    D->>PG: BEGIN · SELECT … FOR UPDATE SKIP LOCKED
    D->>P: ConsumirAsync(mensaje)
    P->>PG: BEGIN · INSERT mensajes_recibidos ON CONFLICT DO NOTHING
    alt mensaje nuevo
      P->>PG: UPSERT saldos_de_reserva (+delta) · COMMIT
    else ya aplicado
      P->>PG: ROLLBACK (sin cambios)
    end
    D->>PG: UPDATE procesado_en (o intentos, siguiente_intento, muerto) · COMMIT
  end
```
