# Integración local ADM-01 + ADM-02 · corte técnico

Fecha del corte: 2026-09-22. Rama local: `integration/adm01-adm02-local`. Este corte combina `main` (`c2412a9`), la rama de Geovany `F1-ADM-01` (`8924366`) y la rama de Uziel `feature/F1-ADM-02-acceso-entra-id` (`f5ad2da`). No equivale a fusión de PR, UAT ni validación con Microsoft Entra real.

## Qué se resolvió para que las ramas convivan

1. `frontend/src/lib/auth/useAuth.ts`: se conservaron la limpieza de caché al cambiar de empresa o cerrar sesión y el estado/redirección de sesión de Entra.
2. `backend/src/Api/Properties/launchSettings.json`: el arranque local común vuelve al modo simulado configurado en Development; se eliminó el OID personal del perfil compartido. Entra se configura de forma explícita para la prueba correspondiente.
3. `AuthSesionEndpointsTests`: el caso de segunda empresa ficticia usa el constructor actual de `Empresa` y limpia sus datos y preferencias al terminar. Este caso prueba aislamiento técnico; para Millet la operación principal es una empresa fiscal con sucursales.
4. Dos pruebas de páginas de Administración recibieron el mock de `useNavigate` que usa el hook de sesión combinado.
5. La migración de ADM-01 ahora inicia desde PostgreSQL vacío: crea una empresa provisional sólo cuando aún no existe ninguna, luego relaciona los catálogos y el bootstrap sustituye los datos provisionales por la configuración inicial de desarrollo. No se insertan datos fiscales reales de Millet.
6. Las pruebas de SharedKernel y sus comentarios se alinearon con el enrutamiento de eventos `admin.*` al outbox de Compartido. Identidad conserva el fallback anterior.
7. El script de integración dejó de mostrar un total fijo de 489 pruebas, porque la suite creció.

## Validaciones ejecutadas en esta combinación

| Gate | Resultado |
| --- | --- |
| Backend API Debug y Release | Compilación correcta, 0 errores y 0 advertencias. |
| Migraciones de 12 DbContexts sobre PostgreSQL temporal vacío | Aplicadas sin error. |
| Integración A+W | 7/7 aprobadas. |
| Integración Compras | 120/120 aprobadas. |
| Integración API | 423/423 aprobadas. |
| Unitarias backend, 12 proyectos | 2,311/2,311 aprobadas. |
| Frontend build y lint | Aprobados. |
| Frontend Vitest | 1,574/1,574 aprobadas en 280 archivos. |
| `git diff --check`, `bash -n` | Aprobados. |

La suite API ejerce endpoints con base real aislada y un doble de validador de token para Entra; no demuestra una sesión contra un tenant de Microsoft. El script elimina el contenedor temporal al terminar.

## Qué falta fuera de este corte

- El modal de Uziel para crear cuentas mediante Microsoft Graph y la invitación por Exchange Online fueron reportados en [Slack](https://bhosolutionsv-m4g8895.slack.com/archives/D0C378T50BS/p1790123372285529), pero no estaban en `f5ad2da`. No se integraron ni se envió ningún correo.
- Geovany indicó que todavía falta la acción directa de vincular un colaborador desde la pestaña de sucursal. Su [resumen](https://bhosolutionsv-m4g8895.slack.com/archives/D0C2FTQ2EJG/p1790124673381469) debe convertirse en cambio publicado y prueba antes de cerrar esa función.
- No se validó el catálogo real de sucursales, tipos de comprobante y series bajo el RFC único de Millet; los datos de esta prueba son ficticios.
- La ficha de ClickUp ADM-01 todavía describe varias razones sociales como escenario principal. Requiere alinearse con la decisión funcional más reciente: Millet opera una entidad fiscal con varias sucursales; la capacidad técnica multiempresa se conserva.
- No se hizo validación visual completa en navegador, UAT de Millet ni despliegue.

## Entrega a Geovany y Uziel

Usar esta rama local y sus resultados como referencia. Incorporar las correcciones a sus ramas o preparar PR de integración revisable, actualizar ambos PR frente a `main` y repetir los gates en CI y con PostgreSQL limpio. Integrar sólo después de revisión cruzada y de decidir qué parte del aprovisionamiento Graph y del correo pertenece a este corte. Los permisos funcionales siguen siendo del ERP; Entra autentica la identidad.
