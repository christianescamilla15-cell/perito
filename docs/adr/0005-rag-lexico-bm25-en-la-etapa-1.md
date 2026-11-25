# ADR 0005 · RAG léxico (BM25) filtrado por ramo en la etapa 1

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
El asistente debe citar la cláusula aplicable de unas condiciones de 40 fragmentos. La recuperación tiene que ser
reproducible en el CI sin modelos ni servicios y medible por separado del LLM.

## Decisión
- BM25 propio (k1 = 1.2, b = 0.75) sobre título y texto de cada cláusula, con normalización del español sin
  `Normalize` (tabla de acentos: con `InvariantGlobalization` no se descompone Unicode) y una raíz ligera.
- Filtro por el RAMO de la póliza (metadato que la aplicación conoce) y cuotas separadas: 4 cláusulas de cobertura y
  3 de exclusión, para que las exclusiones no desplacen a las coberturas.
- El documento de `docs/condiciones` es el corpus: se incrusta en el ensamblado (una sola fuente).

## Alternativas descartadas
- **Embeddings + base vectorial en la etapa 1:** necesita un modelo de embeddings en el CI o un servicio; se mide en la
  etapa 2 contra esta línea base con el mismo conjunto.

## Consecuencias
- Medido: la cláusula esperada está en las 4 recuperadas en 49 de 50 casos (0.98); las exclusiones esperadas, en
  0.82. Los errores de vocabulario (una paráfrasis sin palabras del corpus) son el límite conocido.
- El corpus quedó congelado antes del modelo: no se agregaron palabras para mejorar la recuperación del conjunto.
