#!/usr/bin/env python3
"""Firma un JWT HS256 para probar la API A MANO en local (nunca en producción: ver docs/adr/0008).

    export Perito__Jwt__Llave="$(openssl rand -base64 32)"
    python3 herramientas/token_local.py ajustador ajustador.uno     # imprime el token

La llave sale de la variable Perito__Jwt__Llave (la misma que lee la API); no se escribe en ningún archivo.
"""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import sys
import time

ROLES = {'operador', 'ajustador', 'supervisor', 'tesoreria', 'suscriptor', 'privacidad'}


def b64(datos: bytes) -> str:
    return base64.urlsafe_b64encode(datos).rstrip(b'=').decode()


def firmar(rol: str, usuario: str, llave: bytes, minutos: int = 30) -> str:
    ahora = int(time.time())
    encabezado = {'alg': 'HS256', 'typ': 'JWT'}
    cuerpo = {'iss': 'perito-local', 'aud': 'perito-api', 'sub': usuario, 'rol': rol, 'iat': ahora, 'nbf': ahora,
              'exp': ahora + minutos * 60}
    partes = [b64(json.dumps(x, separators=(',', ':')).encode()) for x in (encabezado, cuerpo)]
    firma = hmac.new(llave, '.'.join(partes).encode(), hashlib.sha256).digest()
    return '.'.join([*partes, b64(firma)])


def main() -> int:
    if len(sys.argv) != 3 or sys.argv[1] not in ROLES:
        print(f'uso: token_local.py <{"|".join(sorted(ROLES))}> <usuario>', file=sys.stderr)
        return 2
    llave = os.environ.get('Perito__Jwt__Llave')
    if not llave:
        print('falta Perito__Jwt__Llave (base64 de 32 bytes o más)', file=sys.stderr)
        return 2
    print(firmar(sys.argv[1], sys.argv[2], base64.b64decode(llave)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
