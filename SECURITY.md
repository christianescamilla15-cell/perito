# Seguridad

## Reportar una vulnerabilidad

Escribe a **christianescamilla15@gmail.com** con el asunto «Seguridad: <proyecto>». Respondo en 72 horas
hábiles y publico el arreglo con su aviso. Por favor no abras un issue público para vulnerabilidades.

## Qué revisa cada cambio

| Revisión | Herramienta | Bloquea |
|---|---|---|
| Análisis estático | CodeQL + Semgrep | sí (Semgrep) |
| Dependencias, configuración e IaC | Trivy | sí, en CRITICAL/HIGH con arreglo publicado |
| Secretos en toda la historia | gitleaks | sí |
| Datos de terceros | revisión de confidencialidad | sí |
| Inventario de componentes | SBOM (syft, SPDX) | no: se publica con cada versión |

Cada revisión debe probar que analizó algo (archivos o paquetes > 0); un reporte vacío cuenta como fallo.
Las excepciones viven en `.trivyignore`, cada una con fecha de vencimiento y motivo.
