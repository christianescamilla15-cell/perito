# ADR 0009 · Groq (API compatible con OpenAI) como el LLM real de la evaluación

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
La compuerta del CI usa una línea base determinista que no es un LLM (ADR 0007). Para saber qué aporta un modelo de
lenguaje real hace falta correr el MISMO conjunto, con el mismo RAG, prompt, función y validaciones, contra un LLM
real, sin infraestructura local pesada y dentro de una capa gratuita con límites estrictos.

## Decisión
- **Proveedor `groq` detrás de la misma abstracción** (`IChatCompletionService`) que la línea base y Ollama: el
  conector OpenAI de Semantic Kernel (`Microsoft.SemanticKernel.Connectors.OpenAI`) con un `OpenAIClient` apuntado a
  `https://api.groq.com/openai/v1`. Nada más del asistente cambia.
- **Modelo `openai/gpt-oss-120b`**, verificado en la documentación de Groq el 2026-11-25: es modelo de producción (no
  preview), admite llamada a funciones (local, no en paralelo: sólo necesitamos una), modo JSON y razonamiento con
  `reasoning_effort`, y tiene caché de prompts. Los Llama 3.x de producción aparecen como «Enterprise» y
  `gpt-oss-20b` tiene los mismos límites de la capa gratuita con menos capacidad. Precio de lista: USD 0.15 por millón
  de entrada, 0.075 en caché y 0.60 de salida.
- **Ajustes:** `temperature = 0`, `reasoning_effort = low` (acota tokens y latencia), `max_completion_tokens = 2048`
  e invocación manual de funciones (la lista blanca de ADR 0004 sigue mandando). No se usa salida estructurada: Groq no
  la admite junto con llamada a funciones; la salida pasa por la misma lectura tolerante y validación.
- **Capa gratuita sin ráfagas:** `ChatConRitmo` deja pasar una solicitud a la vez, separadas 60/RPM s, y no rebasa
  los tokens por minuto de una ventana de 60 s (estimados antes y corregidos con el uso real; lo servido de la caché
  de prompts no cuenta, como dice Groq). Reintentos con `retry-after` (tope de 90 s por espera); si el servidor pide
  más (límite diario), falla rápido y la corrida se reanuda desde la caché de casos (`eval/corridas/`).
- **La llave:** se lee de `~/.config/perito/groq.env` (`GROQ_API_KEY=…`, permisos 600; si el grupo u otros pueden
  leerlo, se niega). `SecretoApi` no se imprime ni se serializa; los errores sólo muestran el estado HTTP. Una prueba
  corre la CLI contra un servidor falso y exige que la llave autorice pero no aparezca en consola, informe ni caché.
- **El CI nunca llama a Groq:** las pruebas del proveedor usan un servidor que habla el protocolo HTTP de OpenAI. La
  compuerta sigue siendo la determinista.

## Consecuencias
- Probado sin red: función ofrecida como `poliza-coberturas_contratadas`; el modelo puede devolverla igual, con guion
  bajo o sin complemento y las tres se ejecutan; otra función se rechaza (el defecto que se encontró con OllamaSharp
  no se repite). Un 429 corto se reintenta; uno de una hora falla rápido sin reintentar.
- El informe `eval/informe/groq.md` lleva fecha, modelo, ajustes, métricas con su control negativo, tokens (con los
  servidos de caché), costo a precio de lista, latencia por llamada y la comparación contra la línea base. Ninguna
  cifra del LLM se publica sin una corrida real.

## Resultado de la corrida del 2026-11-25 (parcial)
- Normal con los 50 casos; el límite diario de tokens (200 mil en la capa gratuita) cortó los controles en 40 casos
  (vacío) y 38 (barajado). Reanudar no fue posible porque la llave se revoca después de la corrida. El informe se
  armó desde la caché de casos (`--solo-cache si`) y compara contra la línea base sobre los mismos casos.
- Contra la línea base determinista: clasificación 0.86 / 0.86, cita 0.90 / 0.86, datos faltantes F1 0.89 / 0.86,
  exhaustividad de exclusiones 0.82 / 0.45. Control: cita 0.00 sin contexto y 0.42 con contexto barajado.
- Uso: 256 llamadas, 268,066 tokens de entrada (110,080 de caché), 30,808 de salida, USD 0.0504 a precio de lista;
  0.72 s de media por llamada en el contexto normal.
- **Defecto encontrado en la corrida real:** con las funciones habilitadas en la respuesta final, gpt-oss intentaba
  «llamar» a una herramienta `json` para entregar su respuesta y Groq devolvía 400 `tool_use_failed`. La ronda final
  ahora va con `tool_choice: none`; una prueba lo exige contra el servidor falso y la línea base no cambió.
- **Hallazgo:** el LLM empata en clasificación porque se abstiene cuando una exclusión aplica o la cobertura no está
  contratada; es decir, decide en lugar de proponer, por una regla ambigua del prompt («si ninguna aplica, usa
  null»). Corregirla y volver a medir es trabajo de la etapa 2 (`eval/analisis-groq.md`).
