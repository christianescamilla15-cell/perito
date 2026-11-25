# Runbook · <proyecto>

## Desplegar

1. Todo cambio entra por PR a `main` con los checks requeridos en verde (ci, seguridad).
2. El despliegue lo hace el pipeline, nunca a mano: preview → pruebas de humo → producción.
3. Después de desplegar: `herramientas/humo.sh <url>` (debe salir con 0).

## Revertir (ensayado el AAAA-MM-DD)

1. <comando exacto para volver a la versión anterior>
2. `herramientas/humo.sh <url>` sobre la versión restaurada.

## Diagnóstico

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| | | |
