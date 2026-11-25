# ADR 0006 · Privacidad por diseño: minimización, cifrado de campo, finalidad, anonimización y seudonimización

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
Un siniestro trae datos personales y, con lesionados, datos de salud. El diseño sigue principios comunes a leyes de
protección de datos tipo LFPDPPP/GDPR (minimización, finalidad, seguridad, derechos del titular) sin pretender
certificar cumplimiento ni citar artículos.

## Decisión
- **Minimización:** el titular guarda nombre, RFC, correo, teléfono y CLABE; el aviso, póliza, fecha, descripción,
  estimación y, sólo si hay lesionados, su detalle.
- **Cifrado de campo** AES-256-GCM para RFC, correo, teléfono, CLABE, descripción y lesiones; el nombre del campo va
  como dato autenticado (un valor no se puede mover de columna); anillo de llaves por id con una activa (rotación).
  Las llaves llegan por variables de entorno; sin ellas no arranca.
- **Finalidad y registro de accesos:** toda lectura de datos personales declara `X-Finalidad` (lista cerrada) y se
  registra quién, rol, recurso, titular y finalidad ANTES de devolver los datos; el registro no guarda los datos.
- **Anonimización a solicitud:** no procede con siniestros abiertos; borra identificadores y texto libre; conserva
  montos, fechas, estados y la cláusula del dictamen.
- **Seudonimización obligatoria antes del LLM:** `TextoSeudonimizado` no tiene constructor público; sólo el
  `Seudonimizador` lo crea (identificadores conocidos del titular, patrones y nombres con señal). El detalle de
  lesiones nunca se envía al asistente.

## Consecuencias
- Probado: sin texto claro en la base; ningún dato personal conocido en los prompts de los 50 casos en 3 contextos;
  anonimizar no deja rastro en ninguna tabla.
- Límites conocidos: un nombre de tercero sin señal («ayer Pedro me chocó») no se detecta (etapa 2: reconocimiento de
  entidades); no hay re-cifrado en lote al rotar (etapa 3); el nombre del titular va en claro para operar.
