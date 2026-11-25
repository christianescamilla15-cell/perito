# ADR 0003 · Bandeja de salida transaccional y consumidor idempotente en PostgreSQL

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
Los cambios del siniestro (reserva, pago, rechazo) deben llegar a otros (un tablero de reservas hoy; contabilidad o
notificaciones mañana) sin perderse ni duplicarse, y sin infraestructura pesada en la etapa 1.

## Decisión
- Cada confirmación guarda el estado del agregado y sus eventos (`mensajes_de_salida`, jsonb) en UNA transacción.
- Un despachador toma lotes con `FOR UPDATE SKIP LOCKED` (varios procesos no se pisan), entrega a los consumidores y
  marca `procesado_en`. Si un consumidor falla: reintento con espera creciente (base × 2^(n−1)) y, al agotar los
  intentos, el mensaje queda **muerto** con su último error.
- Entrega **al menos una vez**. El consumidor (`ProyeccionDeReservas`) es idempotente: en su propia transacción
  inserta `(consumidor, mensaje_id)` en `mensajes_recibidos` con `ON CONFLICT DO NOTHING` y sólo si es nuevo aplica
  el delta. Los deltas son sumas: el orden de llegada no altera el resultado.
- Los eventos llevan ids, ramo, montos y estados; nunca datos personales (prueba de arquitectura y de la base).

## Alternativas descartadas
- **Publicar directo a un bus después de guardar:** si el proceso cae entre los dos pasos, el evento se pierde.
- **Kafka/RabbitMQ en la etapa 1:** el patrón es el mismo con un relé de la bandeja; se agrega cuando haya un segundo
  sistema que lo justifique.

## Consecuencias
- Probado contra PostgreSQL real: un mensaje entregado dos veces se aplica una vez; dos despachadores concurrentes no
  duplican y la proyección cuadra con Σ de las reservas vigentes; reintentos hasta muerto.
- Los mensajes muertos se atienden a mano (RUNBOOK) hasta tener un panel.
