# ADR 0002 · .NET 10 en capas limpias con un dominio que no conoce la persistencia

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
Las reglas de siniestros (vigencia, deducible, coaseguro, fraude, dictamen) tienen que probarse una por una sin base
ni red, y la persistencia necesita cosas que el dominio no debe saber: cifrado de campos, concurrencia por versión y
la bandeja de salida.

## Decisión
- `Perito.Domain` sólo con la BCL: agregados `Poliza`, `Asegurado`, `Ajustador` y `Siniestro` (máquina de estados),
  `Resultado<T>` con errores con nombre, `IClock`, eventos y el `Seudonimizador`. El catálogo del producto se escribe
  una vez en código y una prueba lo compara con los metadatos de `docs/condiciones`.
- `Perito.Application`: casos de uso por un mediador propio (≈60 líneas), puertos, validación de la propuesta del
  asistente y la marca `IComando`/`IConsulta` que usan las pruebas de arquitectura.
- `Perito.Infrastructure`: EF Core 10 + Npgsql. Las FILAS son clases propias; el almacén copia agregado ↔ fila al
  confirmar (rehidratación explícita), cifra sólo lo que cambió y escribe los eventos en la misma transacción.
- `Perito.Api` (Minimal API) compone todo; `Perito.Asistente` es un proyecto aparte que sólo conoce Application.

## Alternativas descartadas
- **Mapear los agregados directo con EF (campos privados, convertidores):** el cifrado con un anillo de llaves no cabe
  en un `ValueConverter` (el modelo de EF se cachea por proceso y el convertidor no recibe servicios), y el dominio
  quedaría atado a las convenciones del ORM.
- **MediatR:** dependencia externa (y de licencia cambiante) para lo que es un diccionario de manejadores.

## Consecuencias
- Más código de mapeo, a cambio de reglas puras: 149 pruebas de dominio corren en ~0.2 s.
- El mapeo se prueba contra PostgreSQL real (ida y vuelta, lectura sin reescritura, concurrencia).
