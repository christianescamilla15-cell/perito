# Runbook · Perito

## Despliegue

**No hay despliegue en la etapa 1**: no hay imagen de contenedor, ambiente ni pipeline de entrega. El CI compila,
prueba, evalúa y escanea cada cambio. La imagen, el despliegue por pipeline con prueba de humo y la reversa ensayada
son la etapa 3 de [`docs/PLAN.md`](docs/PLAN.md).

## Correr la API en local

```bash
export DOCKER_CONFIG=${DOCKER_CONFIG:-$HOME/.docker}
docker run -d --name perito-pg --memory 512m -e POSTGRES_PASSWORD=local -p 127.0.0.1:15432:5432 \
  postgres:16-alpine@sha256:f2f98726820d0342055351c6e9d36e705f5bfffb3dd53ff556325114b08afb6d
export ConnectionStrings__Perito='Host=127.0.0.1;Port=15432;Database=perito;Username=postgres;Password=local'
export Perito__Cifrado__Llaves__k1="$(openssl rand -base64 32)"   # guárdela: sin ella no se descifra nada
export Perito__Jwt__Llave="$(openssl rand -base64 32)"
export Perito__MigrarAlArrancar=true
dotnet run --project src/Perito.Api --urls http://127.0.0.1:5080
```

En el primer arranque EF Core registra un `fail: …Database.Connection[20004]` al comprobar que la base `perito` no
existe; enseguida la crea y aplica las migraciones (esperado; probado el 2026-11-25).

Prueba de humo (otra terminal, con las mismas variables):

```bash
curl -fsS http://127.0.0.1:5080/salud                                  # ok
TOKEN=$(python3 herramientas/token_local.py suscriptor suscriptor.uno)
curl -fsS -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"nombre":"Persona Ficticia","rfc":"FEML850101AB1","correo":"p@example.com","telefono":"5541002233"}' \
  http://127.0.0.1:5080/api/asegurados                                  # 201 con {"id": …}
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:5080/api/asegurados      # 401 sin token
```

Al terminar: `docker rm -f -v perito-pg` (también su volumen).

Con Ollama en lugar de la línea base: `export Perito__Asistente__Proveedor=ollama Perito__Asistente__Modelo=qwen2.5:1.5b`
y un `ollama serve` local con ese modelo descargado.

## Evaluación del asistente

```bash
dotnet run --project src/Perito.Evaluacion -c Release -- --compuerta eval/umbrales.json   # regenera eval/informe/extractivo.*
dotnet run --project src/Perito.Evaluacion -c Release -- --proveedor ollama --modelo qwen2.5:1.5b
```

Si cambia una regla, el prompt o el corpus, el informe versionado cambia: se revisa el diff de cifras y se versiona
en el mismo commit (el CI exige que coincidan).

## Rotar la llave de cifrado

1. Agregar la nueva llave al anillo sin quitar la anterior: `Perito__Cifrado__Llaves__k2=…` y
   `Perito__Cifrado__LlaveActiva=k2`. Lo nuevo se cifra con `k2`; lo viejo se sigue leyendo con `k1`.
2. Lo cifrado con `k1` se re-cifra cuando su texto cambia. **No hay re-cifrado en lote** (etapa 3): mientras exista
   un valor con `k1`, `k1` no se puede retirar. `CifradorDeCampos.LlaveDe` dice con qué llave quedó cada valor.

## Diagnóstico

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| La API no arranca: «Falta la configuración …» | falta un secreto por variable de entorno | definir `ConnectionStrings__Perito`, `Perito__Jwt__Llave` y `Perito__Cifrado__Llaves__<id>` |
| 401 con un token recién firmado | llave distinta a la de la API, emisor/audiencia o reloj | firmar con la misma `Perito__Jwt__Llave`; emisor `perito-local`, audiencia `perito-api` |
| 400 `finalidad_invalida` al leer un siniestro o titular | falta `X-Finalidad` | `atencion_del_siniestro`, `derechos_del_titular` o `auditoria` |
| 409 `conflicto` | otro cambio guardó el mismo siniestro primero | volver a consultar y repetir la acción |
| El tablero no se mueve | trabajador apagado (`Perito__Salida__Habilitada=false`) o mensajes muertos | `SELECT id, tipo, intentos, ultimo_error FROM perito.mensajes_de_salida WHERE muerto;` corregir la causa y reencolar con `UPDATE perito.mensajes_de_salida SET muerto = false, intentos = 0, siguiente_intento = NULL WHERE id = …;` (el consumidor es idempotente) |
| 429 al pedir propuestas | límite por usuario y minuto | esperar o subir `Perito__LimiteDePropuestasPorMinuto` |
| Propuestas vacías con Ollama | servidor caído o modelo sin descargar | `curl 127.0.0.1:11434/api/tags`; las observaciones de la propuesta dicen qué falló |
