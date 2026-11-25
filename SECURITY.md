# Seguridad

## Reportar una vulnerabilidad

Escribe a **christianescamilla15@gmail.com** con el asunto «Seguridad: Perito». Respondo en 72 horas hábiles y
publico el arreglo con su aviso. Por favor no abras un issue público para vulnerabilidades.

## Qué revisa cada cambio

| Revisión | Herramienta | Bloquea |
|---|---|---|
| Análisis estático | CodeQL (C# compilado y Python) + Semgrep `p/default` | sí (Semgrep) |
| Dependencias, configuración e IaC | Trivy sobre los `packages.lock.json` | sí, en CRITICAL/HIGH con arreglo publicado |
| Secretos en toda la historia | gitleaks | sí |
| Datos de terceros | revisión de confidencialidad | sí |
| Inventario de componentes | SBOM (syft, SPDX) | no: se publica con cada versión |

Cada revisión debe probar que analizó algo (archivos o paquetes > 0); un reporte vacío cuenta como fallo.
Las excepciones viven en `.trivyignore`, cada una con fecha de vencimiento y motivo.

## Controles del producto

- **Secretos fuera del código:** cadena de conexión, llaves de cifrado y llave de firma por variables de entorno; sin
  ellas la API no arranca (probado).
- **Identidad y roles:** JWT HS256 con emisor, audiencia y vigencia; políticas por ruta; quien dictamina no paga
  (ADR 0008: llave simétrica sólo para la etapa 1).
- **Datos personales:** cifrado de campo AES-256-GCM con el campo como dato autenticado, finalidad declarada y
  registro de accesos, anonimización a solicitud y seudonimización obligatoria antes de cualquier prompt (ADR 0006).
- **Asistente:** propone y no decide; una sola función de lectura en lista blanca, tope de rondas y de tiempo,
  validación de la salida y límite de propuestas por usuario y minuto (ADR 0004).
- **Errores:** problem+json con código, sin excepciones ni cadenas de conexión en la respuesta (probado).
