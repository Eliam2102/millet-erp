# Publicación y seguridad del repositorio

## Condición de publicación

- Repositorio privado bajo el perfil autorizado de Eliam.
- Sin secretos, certificados, respaldos, dumps ni datos reales rastreados.
- Despliegues a Azure exclusivamente manuales hasta autorización expresa.
- Cambios por pull request y revisión. La protección automática de `main` queda pendiente por la limitación del plan actual de GitHub para repositorios privados.
- La versión entregada se identifica con commit y etiqueta inmutables.

## Revisión realizada antes de publicar

- Se revisaron archivos rastreados, configuración local, parámetros Bicep y referencias históricas a claves.
- Las claves de servicios on-prem observadas están vacías; las contraseñas de infraestructura se obtienen por variable de entorno o Key Vault.
- Los valores locales `pgadmin/pgadmin` son exclusivamente para PostgreSQL de desarrollo.
- El repositorio sigue siendo sensible por contener arquitectura, nombres de recursos, IDs públicos de aplicaciones y contratos de integración; por ello no debe hacerse público.

## Reglas para los desarrolladores

1. Nunca guardar secretos en Git, issues, PR, ClickUp, Notion o capturas.
2. Usar archivos locales ignorados o el almacén seguro aprobado.
3. Si un secreto aparece en un commit, detener publicación, revocarlo y sanear el historial; borrarlo en un commit posterior no es suficiente.
4. No ejecutar workflows manuales de despliegue sin ambiente, autorización y plan de reversión confirmados.
5. No usar datos productivos en desarrollo local.

## Verificación de accesos

ClickUp y Notion fueron confirmados por Eliam como accesibles para Geovany y
Uzziel. La verificación del repositorio es independiente: la consulta de
colaboradores del repositorio privado realizada el 16 de septiembre de 2026
mostró únicamente a `Eliam2102` y ninguna invitación pendiente.

Para cerrar GitHub se requieren los usuarios exactos de Geovany y Uzziel. No
se inferirán por nombre o correo. Después de invitarlos, cada uno debe comprobar
MFA, clonación, arranque, login y pruebas con su propia cuenta y equipo, y dejar
la evidencia en su tarea de Ola 0.

## Limitación comprobada

El intento de aplicar branch protection mediante la API devolvió HTTP 403 y solicitó GitHub Pro o hacer público el repositorio. Hacerlo público no es aceptable por la sensibilidad del proyecto. La solución correcta es habilitar un plan compatible o mover el repositorio privado a una organización que permita las reglas; hasta entonces se debe auditar que los cambios entren por PR.
