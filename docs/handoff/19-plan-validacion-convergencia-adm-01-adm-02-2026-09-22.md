# Borrador para validar · Convergencia ADM-01 + ADM-02

Fecha del corte: 2026-09-22. Objetivo: obtener hoy una integración local demostrable y entregar mañana (2026-09-23) a Geovany y Uziel una lista cerrada de correcciones y pruebas para integrar sus PR. Este documento es un plan de validación; no declara aceptados los módulos ni autoriza cambios en Microsoft, correos, ClickUp o `main`.

## Fuentes y estado verificado

- Base: `main` en `c2412a9` (PR #2 de estabilización integrado).
- Geovany: `origin/F1-ADM-01` en `8924366`; [resumen privado](https://bhosolutionsv-m4g8895.slack.com/archives/D0C2FTQ2EJG/p1790124673381469); [ADM-01](https://app.clickup.com/t/86e3a6c9g) y [sucursal master-detail](https://app.clickup.com/t/86e3d1rfq). Rama con 276 archivos cambiados frente a `main`, incluyendo migraciones y cambios transversales.
- Uziel: `origin/feature/F1-ADM-02-acceso-entra-id` en `f5ad2da`, [PR #3](https://github.com/Eliam2102/millet-erp/pull/3); [resumen privado](https://bhosolutionsv-m4g8895.slack.com/archives/D0C378T50BS/p1790123372285529); [ADM-02](https://app.clickup.com/t/86e3a6c9k). El modal de alta mediante Microsoft Graph y el correo de invitación descritos en Slack aún no están en la rama publicada.
- Simulación local: fusionar ambas ramas sobre una copia temporal produce un conflicto de contenido en `frontend/src/lib/auth/useAuth.ts`. Se preparó una resolución local que conserva limpieza de caché, estado de cambio de empresa y cierre de sesión. Con ella compilan el API (`Debug`, 0 errores y 0 advertencias) y el frontend; lint también pasa. La suite completa sigue pendiente de corregir y aprobar.
- El gate de integración con PostgreSQL aislado **no llegó a las pruebas**: al compilar la solución, `AuthSesionEndpointsTests.cs:326` llama al constructor antiguo de `Empresa`, incompatible con el modelo de Geovany (`CS7036`, falta el argumento `calle`). Esta es una incompatibilidad de las ramas, no un problema del entorno. Además, ese test crea una segunda empresa y no la limpia.
- Primera corrida de Vitest en la mezcla: 1,570 pruebas pasan y 4 fallan (2 archivos). Los mocks de `@tanstack/react-router` en las pruebas de `SucursalesTopLevelPage` y `DepartamentosTopLevelPage` no exponen `useNavigate`, que ahora consume el hook de sesión de Uziel. Se comprobó en la copia temporal que agregar ese mock hace pasar las 4 pruebas dirigidas; la suite completa debe repetirse después de la corrección definitiva.
- La ficha de ClickUp todavía describe varias razones sociales; la decisión funcional más reciente para Millet es **una entidad fiscal con un RFC y varias sucursales**. La infraestructura `EmpresaId` permanece para aislamiento técnico y posible reutilización. La decisión debe reflejarse en criterios de aceptación y datos de prueba.

## Qué se busca demostrar

Con datos ficticios y servicios simulados: el administrador prepara la empresa Millet y sus sucursales, departamentos, puestos y colaboradores; crea o vincula un usuario interno; le asigna rol y sucursal; ese usuario inicia sesión mediante identidad simulada, ve sólo lo autorizado y recibe el estado de una invitación preparada sin enviar correo real. La ruta de Entra y Microsoft Graph se valida por separado cuando haya tenant, permisos y cuentas de prueba autorizadas.

## Secuencia de trabajo

| Corte | Acción | Evidencia mínima | Responsable propuesto |
| --- | --- | --- | --- |
| 1. Congelar entrada | Registrar SHA de `main`, las dos ramas y PR #3; recuperar el commit del modal Graph cuando Uziel lo publique. | SHA, lista de cambios y alcance exacto. | Revisión técnica / Uziel |
| 2. Convergencia local | Integrar en worktree aislado: `main` + ADM-01 + ADM-02. Resolver `useAuth.ts` preservando cambio de contexto, caché y logout. Revisar configuraciones y migraciones. | Diferencia sin marcadores de conflicto; compilación; lista de conflictos semánticos. | Revisión técnica; Geovany y Uziel validan su zona |
| 3. Gate técnico | Ejecutar build backend/frontend, lint, pruebas unitarias y la integración aislada con PostgreSQL nuevo y todas las migraciones. Aislar la prueba de sesión que deja una segunda empresa en la base. | Resultado por suite, migraciones aplicadas, errores con archivo y responsable. | Revisión técnica |
| 4. Demo funcional local | Recorrer la matriz de abajo usando una empresa Millet con varias sucursales y usuarios ficticios. Repetir el caso de dos empresas ficticias sólo para probar que `EmpresaId` no mezcla datos. | Capturas o grabación corta, llamadas API, resultados esperados/reales. | Geovany + Uziel + revisión |
| 5. Cierre de brechas | Geovany completa vinculación directa de colaborador desde sucursal. Uziel publica Graph/invitación si ese alcance queda autorizado y corrige los bloqueos de PR #3. | Commits y PR actualizados; prueba de regresión. | Cada autor |
| 6. Entrega a mañana | Cada PR apunta a `main`, CI verde y revisión cruzada. Integrar en orden acordado, actualizar `main` después del primer merge y repetir gates sobre el segundo. | Dos PR revisados, checks, evidencia enlazada a tareas y decisión de merge. | Geovany + Uziel + revisor |

## Matriz de aceptación de la demo

| Flujo | Debe comprobarse | Límite de la simulación |
| --- | --- | --- |
| Empresa y RFC | Se crea/edita la empresa Millet; una sucursal siempre pertenece a esa empresa; las series se asocian a sucursal y tipo de comprobante sin cambiar el RFC emisor. Validar campos, duplicados e inactivación. | RFC, sucursales y series reales: por confirmar con Millet. |
| Organización | Crear sucursal, departamento y puesto; asignar puesto/departamento y colaborador a sucursal; separar colaborador operativo de usuario con acceso ERP. | La asignación directa de colaborador desde la pestaña de sucursal aún falta. |
| Usuario ERP | Crear/vincular usuario interno por `oid`, evitar duplicados, asignar rol y sucursal; impedir acceso si está inactivo, sin rol o sin alcance. | Crear una cuenta en Entra por Graph es un flujo distinto y no está publicado. |
| Inicio y cierre de sesión | Login simulado local, empresa/sucursal visibles según permisos, logout y redirección correctos; cambio de contexto descarta datos previos. | No demuestra login real con Entra. |
| Autorización | Operativo sólo ve sus sucursales; corporativo ve lo permitido en todas; acceso directo a API sin permiso devuelve rechazo. Caso de dos empresas ficticias comprueba aislamiento técnico. | Alcance exacto de cada rol y permiso: confirmar contra matriz funcional. |
| Invitación/notificación | Preparar evento, destinatario, plantilla y estado de envío; verificar duplicados, fallas y reintentos con adaptador simulado. | No enviar correo real ni crear cuentas en el tenant durante esta validación. |

## Bloqueos que impiden declarar listo el merge

1. PR #3 fuerza `Auth__Mode=EntraId` y un OID de sandbox personal en el perfil compartido `launchSettings.json`. El arranque local común debe conservar un modo simulado y configurar Entra sólo en un perfil explícito o configuración no versionada.
2. `AuthSesionEndpointsTests` deja de compilar al combinarse con ADM-01 por el constructor de `Empresa`. Reescribir el setup para el modelo actual, aislar datos o revertirlos en `finally`, y agregar el escenario principal de Millet único con varias sucursales.
3. Actualizar los mocks de router de las dos pruebas de Administración que fallan por `useNavigate`, y repetir la suite de frontend. La primera corrida sobre la mezcla fue 1,570 aprobadas / 4 fallidas.
4. El cambio de Geovany hizo que `/api/dev/fake-login` esté presente también en binarios Release, con guardas de entorno. Comprobar expresamente que sólo responde en Development y modo simulado; verificar comportamiento en un entorno no Development.
5. Graph e invitación por correo están en tareas [ADM-02.8](https://app.clickup.com/t/86e3cwdu1) y [ADM-02.9](https://app.clickup.com/t/86e3d0gya), pero no en el PR publicado. Requieren diseño de cuenta existente/nueva, permisos mínimos de tenant, errores parciales, reintentos e idempotencia. No incluir contraseñas temporales en logs, repositorio o mensajes.
6. El documento de ClickUp para ADM-01 exige dos empresas como caso principal. Actualizarlo para que la aceptación de Millet sea una empresa fiscal con sucursales, dejando dos empresas ficticias como prueba técnica de aislamiento.
7. Los checks actuales de GitHub no ejecutan la suite de integración. Un check verde de PR no sustituye el gate de base de datos ni la demo local.

## Decisiones necesarias para aprobar el alcance

- Confirmar si el ERP **sólo vincula** usuarios existentes de Entra o también **crea cuentas nuevas** en el tenant mediante Microsoft Graph. Esto amplía el alcance originalmente descrito para ADM-02.
- Confirmar si el correo de invitación debe salir desde Exchange Online, quién autoriza la plantilla/remitente y cómo se verifica entrega; por ahora se prueba sólo la preparación simulada.
- Confirmar matriz de usuarios operativos y corporativos por sucursal, y catálogo real `sucursal × tipo de comprobante × serie` bajo el RFC único de Millet.

## Criterio de salida para mañana

Se puede pedir la integración de los PR cuando la fusión local y la solución completa compilen, las suites relevantes pasen, la demo principal funcione con datos ficticios, no queden configuraciones personales compartidas, las migraciones se apliquen desde una base limpia y cada brecha esté corregida o explícitamente fuera del corte. Al corte actual **todavía no se cumple**: hay al menos una prueba de integración que no compila al combinar las ramas. `Integrado técnicamente` no equivale a validación real de Entra, entrega de correo, UAT de Millet o producción.
