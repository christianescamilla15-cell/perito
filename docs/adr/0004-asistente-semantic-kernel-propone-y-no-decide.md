# ADR 0004 · Asistente con Semantic Kernel: propone y no decide

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
El triaje lee una descripción libre. Un LLM ayuda a clasificar y a pedir lo que falta, pero un dictamen sobre dinero
de un asegurado no puede depender de una salida no determinista, y las condiciones generales (GEN-12) dicen que
ninguna decisión se toma sólo con un tratamiento automatizado.

## Decisión
- El asistente devuelve una **propuesta** (tipo, cláusula citada, datos faltantes, exclusiones a verificar,
  señales). Se guarda como documento; el siniestro no cambia. El triaje lo pide una persona con un tipo y el dominio
  recalcula cobertura, elegibilidad, indicios, reserva y dictamen; si la persona usó la propuesta, queda su id.
- **Llamada a funciones con invocación manual** (`FunctionChoiceBehavior.Auto(autoInvoke: false)`): el bucle ejecuta
  SÓLO la función de la lista blanca (`poliza-coberturas_contratadas`), sin parámetros y acotada al siniestro, que
  consulta la carátula por una consulta de Application. Cualquier otra se rechaza y se anota; hay tope de rondas y de
  tiempo.
- **Validación de la salida en Application:** sólo valores del catálogo del ramo; la cita sólo si está entre las
  cláusulas recuperadas y ampara el tipo; la cobertura y si está contratada las calcula la aplicación.
- **Proveedor intercambiable** por `IChatCompletionService`: `extractivo` (línea base determinista, no es un LLM:
  lee el prompt y elige por coincidencia ponderada) para pruebas y CI; `ollama` en local. El servicio dedicado del
  conector de Ollama de SK (alfa) está marcado obsoleto y recomienda el `IChatClient` de OllamaSharp adaptado con
  `AsChatCompletionService()`: eso se usa, con OllamaSharp estable en lugar del paquete alfa.
- No se incluye Azure OpenAI ni otro proveedor: no está probado.

## Consecuencias
- Pruebas: 6 propuestas hostiles no cambian el siniestro (en memoria y en PostgreSQL: xmin y bandeja intactos);
  aceptar una propuesta falsa termina en el dictamen que dictan las reglas; en el IL, el asistente no crea comandos
  ni cambia agregados (y la prueba falla si se inyecta uno).
- La calidad del modelo se mide aparte (ADR 0007); la compuerta del CI usa la línea base determinista.
- La ruta de Ollama se prueba contra un servidor que habla su protocolo HTTP (`/api/chat`) sin modelo: viajan la
  herramienta, `temperature: 0` y el resultado de la función, sin datos personales. Esa prueba encontró un defecto:
  con el adaptador de `IChatClient` la llamada llega como `poliza_coberturas_contratadas` sin complemento, la lista
  blanca la rechazaba y el modelo nunca recibía la carátula; ahora la función se resuelve por su nombre canónico o
  el del adaptador.
- No hubo corrida con un modelo real en este corte: Ollama no está instalado en la máquina de trabajo, el disco
  estaba al 99 % y la descarga iba a ~350 KB/s. El comando está en el RUNBOOK.
