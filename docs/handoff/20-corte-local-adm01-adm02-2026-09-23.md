# ADM-01 / ADM-02 — corte local para validación (borrador interno)

Fecha: 2026-09-23. Rama aislada: `fix/cierre-adm01-adm02-seguridad-flujo`.
No equivale a merge, despliegue, UAT ni cierre de ClickUp.

## Alcance construido en esta rama

- Alta unificada: empleado con cuenta Microsoft existente, cuenta nueva o sin acceso; rol y sucursal se asignan en el mismo corte de datos del ERP.
- Camino nuevo: el worker crea la cuenta por Graph (cuando se elige explícitamente el proveedor real), escribe el OID devuelto, solicita el correo con contraseña temporal aleatoria y marca cambio obligatorio en el primer inicio. Reintento adopta la cuenta **solo** si su `employeeId` coincide con la clave del empleado; no adopta un UPN ajeno.
- El envío fallido no se disfraza de éxito. El estado queda en error y el administrador puede reintentar o generar una contraseña nueva y reenviar. Graph `202 Accepted` demuestra aceptación de la solicitud, **no entrega al buzón**.
- Login F5: el token Entra real puede vincular el OID de un usuario pendiente por correo y conservar `Usuario.Id`, rol y sucursal; fake login no puede usar ese respaldo.
- Gestión F6: “Dar acceso” posterior a un empleado sin usuario; PATCH sincroniza departamento; baja laboral bloquea el usuario; la recontratación no reactiva el acceso automáticamente. El PATCH genérico ya no permite cambiar `UsuarioId` a mano.
- UI F7: alta en cinco pasos, validación de correo, rol sugerido desde puesto, sección Acceso en empleados con estado, dar acceso, reintentar, reenviar y reactivar usuario según permiso.
- F8: `tools/reporte-reconciliacion-colaboradores.sql` clasifica A–G y lista F/G sin escribir. **No limpia datos ni sustituye la confirmación humana de B/E/F/G**.

## Verificación local

- API .NET: compilación sin errores ni advertencias.
- Integración API: 472/472 en PostgreSQL de prueba aislado `millet_adm_cierre_test_20260923` (puerto local 5434). No se usó la base de otro proyecto como evidencia.
- Unitarias: Administración 120/120; Identidad 71/71.
- Frontend: build de producción y 1,588/1,588 pruebas, repetidas después del último ajuste del formulario.
- Contrato Graph sin tenant: pruebas con HTTP simulado para POST/GET/PATCH de usuario, protección de cuenta ajena y correo aceptado/rechazado.
- Reporte A–G ejecutado en la base **de pruebas**, no QA: A=58, C=7, D=1, E=31, F=0, G=0. Estos números **no** representan datos de Millet.

## Configuración real requerida, sin secretos en el repositorio

1. `Entra:Proveedor=Graph` y `Entra:Provision:Disabled=false` por ambiente. La configuración por defecto permanece `Simulado`/desactivado; así no se crean cuentas ni se finge envío accidentalmente.
2. `Auth:EntraId:TenantId`, `ClientId` y `ClientSecret` de la aplicación aprobada por TI, inyectados desde el gestor de secretos; `SenderEmail` de un buzón Exchange con permiso de envío.
3. `Entra:DominiosPermitidos` con los dominios reales confirmados por Millet y `Entra:UrlInicioSesion` HTTPS del ERP.
4. Consentimiento de administrador para los permisos Graph necesarios (`User.ReadWrite.All`, `Mail.Send` y el permiso de actualización de `passwordProfile`); TI confirma privilegios de rol, licencia/`usageLocation`, política de contraseña y MFA.
5. Prueba controlada con correo personal de prueba: alta, recepción real, cambio obligatorio en Microsoft, regreso al ERP con OID/rol/sucursal correctos; prueba de falla de buzón, reintento y baja. No usar a una persona real sin autorización.

## Pendientes que impiden marcar ADM-01/02 como cerradas

- Ejecutar A–G en QA y obtener cero F/G; revisar manualmente B/E y aprobar cualquier limpieza. El reporte local no cambia datos.
- Prueba real de Graph/Exchange y del primer login con el tenant de Millet. No hay credenciales ni consentimiento disponibles en esta rama.
- UAT visual del wizard y la sección Acceso con el cliente; verificar textos y permisos por perfil. La lista `/admin/usuarios` aún no se ha recortado a “Cuentas de acceso” con marca de empleado vinculado.
- Aprobación del owner de ADR-0052 (sigue en Propuesta) y conciliación de su texto: la implementación F4 usa la fila `ProvisionandoCuenta` como cola, no el evento de outbox descrito en un párrafo anterior del ADR.
- No se han movido tareas de ClickUp, enviado mensajes, publicado la rama ni creado PR. Requieren revisión/autorización de Eliam.

## Criterio de cierre recomendado

Solo cerrar cuando la rama se revise, QA tenga A–G sin F/G, Graph y el correo real pasen con usuario de prueba, el primer login cambie contraseña y conceda únicamente rol/sucursal esperados, el owner apruebe ADR-0052 y Eliam dé VoBo de UAT. Entonces publicar la rama de revisión, sin merge automático.
