# ADR 0008 · Autenticación JWT con llave simétrica en la etapa 1

- Estado: aceptada · Fecha: 2026-11-25

## Contexto
La API maneja datos personales y separa funciones (quien dictamina no paga), así que necesita identidad y roles desde
el primer corte, sin depender de un proveedor de identidad externo para correr las pruebas.

## Decisión
JWT HS256 validado con emisor, audiencia, vigencia (30 s de tolerancia) y llave de al menos 256 bits por variable de
entorno; roles en el claim `rol` (operador, ajustador, supervisor, tesorería, suscriptor, privacidad) y políticas por
ruta. Las pruebas firman sus tokens; `herramientas/token_local.py` firma uno para probar a mano en local.

## Alternativas descartadas
- **API sin autenticación:** el registro de accesos y la separación de funciones no tendrían a quién atribuir.
- **OIDC en la etapa 1:** se agrega en la etapa 3 (llaves asimétricas del proveedor y rotación sin redespliegue).

## Consecuencias
Quien tenga la llave puede firmar cualquier rol: aceptable en local y en pruebas, no en producción.
