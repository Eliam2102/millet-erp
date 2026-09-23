# Continuidad ADM-01 + ADM-02 — rama local de cierre

Fecha de corte: 2026-09-22. Rama: `feature/completar-adm01-adm02-provisionamiento`, creada desde `integration/adm01-adm02-local` (`b621d99`). No se ha publicado ni fusionado en `main`.

## Fuente y criterio

- Mensaje privado de [Geovany](https://bhosolutionsv-m4g8895.slack.com/archives/D0C2FTQ2EJG/p1790124673381469): la pestaña Colaboradores ya mostraba empleados asignados, pero carecía de una acción directa para vincularlos.
- Mensaje privado de [Uziel](https://bhosolutionsv-m4g8895.slack.com/archives/D0C378T50BS/p1790123372285529): reportó un modal temporal de validación/creación por Graph y el correo como pendiente de verificar. Tras `git fetch` del 22/09, `origin/feature/F1-ADM-02-acceso-entra-id` permanece en `f5ad2da`; ese modal no está publicado en la rama.
- Decisión funcional vigente: para Millet, una entidad fiscal y varias sucursales operativas. Se conserva `EmpresaId` para el aislamiento técnico. Entra autentica; usuarios, empresa activa, roles y permisos son del ERP.

## Cambios locales de esta rama

1. Desde la ficha de sucursal se puede vincular un colaborador activo, sin sucursal, de la empresa activa; también se puede dar de alta uno nuevo con esa sucursal preseleccionada. No se mueve automáticamente a alguien ya asignado a otra sucursal.
2. El servidor comprueba que puesto, sucursal y departamento pertenecen a la misma empresa del colaborador y están activos antes de guardar la relación.
3. Hay un adaptador de lectura a Microsoft Graph para encontrar una cuenta por correo/UPN y obtener su `oid`; rechaza una cuenta deshabilitada o una coincidencia ambigua. El formulario muestra el resultado de la consulta; el endpoint exige permiso de creación de usuarios. Las credenciales de la aplicación sólo se toman de configuración externa (`Identidad:EntraDirectory`), sin secretos en el repositorio.
4. En modo real, el alta ERP ya no inventa un `dev-{email}` cuando Graph no encuentra la cuenta, y verifica que un Object ID manual corresponda al correo. El placeholder se permite sólo en el modo fake de Development.

## Pendiente para un alta de cuenta nueva y notificaciones

No se creó ni activó una cuenta Microsoft, ni se envió correo real. La creación de una cuenta nueva requiere acordar el destinatario alterno verificado para el primer acceso, el dominio/UPN, el procedimiento de entrega de credenciales o Temporary Access Pass y la política/permisos del tenant. No debe enviarse una contraseña inicial por correo ni incrustarla en código. Microsoft Graph `POST /users` crea la identidad, pero por sí solo no entrega una vía segura de acceso a la persona.

El correo de cuenta existente podría avisar que el acceso ERP fue asignado y enlazar al inicio de sesión (sin contraseña). El correo de cuenta nueva debe salir únicamente después de definir la vía de activación y el receptor alterno; enviarlo a la bandeja recién creada no garantiza que la persona pueda leerlo. Se requieren permisos Graph de lectura/escritura de usuarios, envío limitado al buzón autorizado y, si se adopta TAP, su política y permiso específicos. Estas decisiones y la prueba real con TI de Millet están **Por confirmar**.

Borradores de contenido para autorización, **no enviados**:

- Cuenta existente — asunto: `Tu acceso al ERP de Vidrios Millet`. Texto: `Hola, {nombre}. Tu usuario de Microsoft {correo} ya fue vinculado al ERP. Ingresa con tu cuenta habitual en {urlERP}. Si no puedes entrar, contacta a TI; no respondas con contraseñas ni códigos.`
- Cuenta nueva — asunto: `Alta de cuenta para el ERP de Vidrios Millet`. Texto para receptor alterno verificado: `Hola, {nombre}. Se preparó una cuenta Microsoft {upn} para tu acceso al ERP. TI te entregará por un canal verificado el método de primer ingreso. Una vez activada, entra en {urlERP}. Este mensaje no contiene contraseñas.`

Falta aprobar remitente, destinatario, URL, plantillas, política de reintentos/idempotencia y evidencia de entrega. Hasta entonces el sistema no debe afirmar `correo enviado`.

## Verificación y entrega

Verificación local:

- Backend Debug: compilación de solución sin errores ni advertencias.
- PostgreSQL temporal vacío: aplicadas las migraciones de los 12 `DbContext`.
- Integración: A+W 7/7, Compras 120/120, API 424/424. La primera corrida detectó dos pruebas que dependían de una segunda empresa creada por otra prueba; ambas ahora crean su propia empresa ficticia y el gate completo pasó en la segunda corrida.
- Unitarias .NET: 2,311/2,311 en 12 proyectos.
- Frontend: build, lint y Vitest 1,575/1,575 en 280 archivos. La prueba nueva de vinculación de colaborador desde la ficha de sucursal pasó.
- `git diff --check`: sin errores.

Después, revisión cruzada de Geovany/Uziel sobre esta rama; repetir las pruebas en CI y probar contra un tenant de ensayo y un buzón controlado **sólo con autorización**. No confundir compilación o Graph simulado con E2E, aceptación de Millet o producción.
