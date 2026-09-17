# Publicación y seguridad del repositorio

## Condición de publicación

- Repositorio privado bajo el perfil autorizado de Eliam.
- Sin secretos, certificados, respaldos, dumps ni datos reales rastreados.
- Despliegues a Azure exclusivamente manuales hasta autorización expresa.
- `main` protegida; cambios por pull request y revisión.
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

## Accesos pendientes

Para invitar a Geovany y Uzziel se requieren sus nombres exactos de usuario en GitHub. No se inferirán por nombre o correo. Después de invitarlos, cada uno debe comprobar MFA, clonación, arranque, login y pruebas con su propia cuenta y equipo.
