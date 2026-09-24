# ADM-01/02 — entrega local para pruebas (actualizado 2026-09-24)

Rama: `fix/cierre-adm01-adm02-seguridad-flujo`. Este documento describe el corte local; no equivale a QA aprobado, integración con el tenant de Millet, UAT, merge ni despliegue.

## Evidencia de construcción local

- Backend: compilación correcta sin errores ni advertencias; 473/473 pruebas de integración API en la base de prueba aislada `millet_adm_cierre_test_20260923`, incluidas la protección del ingreso falso fuera de modo FakeForLocalDev.
- Frontend: build de producción correcto; 1,589/1,589 pruebas en 288 archivos, repetidas tras el cambio de sesión local.
- La prueba de integración de `Dar_Acceso_A_Empleado_Existente_Vincula_Usuario_Rol_Y_Sucursal` comprueba `empleadoId` en listado y detalle; la prueba de UI distingue cuenta vinculada de cuenta sin vínculo.
- Se ejecutó una prueba funcional local con datos ficticios: sucursal, departamento y puesto vinculados; alta de empleado con cuenta nueva; worker de provisión; captura del correo en Mailpit; rol y sucursal persistidos; ingreso simulado del mismo usuario. También se comprobó en navegador que **Cuentas de acceso** muestra el vínculo real y que **Simular ingreso** cambia la sesión al colaborador. La contraseña no se muestra en el ERP.
- La prueba automatizada de recorrido se repitió con otro colaborador ficticio y confirmó correo capturado al destinatario correcto. Ninguna de estas pruebas es aceptación del cliente.

## Qué debe poder revisar el equipo mañana

1. En **Empleados**, dar de alta un colaborador sin acceso, con cuenta Microsoft ya existente en el simulador o con cuenta nueva. Comprobar que sucursal, departamento, puesto y rol se guardan y que la cuenta nueva pasa de pendiente a aprovisionada cuando Mailpit está disponible.
2. En **Cuentas de acceso**, localizar el usuario creado y verificar la marca **Empleado vinculado**. Las cuentas históricas sin vínculo se muestran como **Sin empleado vinculado**; no se clasifican automáticamente como cuentas técnicas.
3. Abrir la cuenta y comprobar las asignaciones y el estado activo/inactivo. En el empleado, abrir la sección **Acceso** y probar las acciones permitidas (dar acceso, reintentar, reenviar o reactivar según estado y permiso).
4. Confirmar que una baja laboral bloquea el usuario y que la recontratación no reactiva el acceso automáticamente. Validar que una persona sin permiso no ve ni ejecuta las acciones restringidas.
5. Abrir el correo en Mailpit y comprobar destinatario y contenido. Desde la cuenta activa, usar **Simular ingreso** y comprobar identidad y permisos del colaborador; esta acción solo existe en Development/FakeForLocalDev. Ejecutar regresión y anotar resultado, entorno, caso fallido y evidencia. El reporte A–G es solo de diagnóstico; cualquier corrección B/E/F/G requiere revisión y decisión humana.

## Límite de la simulación

El proveedor `Simulado` permite comprobar ambos caminos. En Development, con `Auth:Mode=FakeForLocalDev`, la provisión automática solo arranca si SMTP apunta a loopback; el correo se captura en Mailpit, no se entrega al destinatario externo. En otros ambientes, el worker continúa apagado por defecto y la provisión simulada con correo queda prohibida. El endpoint de ingreso falso responde 404 si la API está en modo Entra, aun dentro de Development. La cuenta simulada no existe en Microsoft: el ingreso de prueba no exige la clave recibida ni ejercita MFA o cambio obligatorio. El correo de prueba la identifica expresamente como **clave simulada no utilizable en Microsoft**. La recepción real del correo, el cambio de contraseña en Microsoft y el primer acceso con OID real requieren un tenant y buzones de prueba autorizados; **siguen pendientes** y no deben presentarse como validados.

El directorio simulado mantiene en memoria las cuentas creadas; al reiniciar la API, conserva el vínculo OID en la base ERP, pero no la cuenta ficticia del directorio. Por tanto, probar **reenviar/restablecer acceso después de un reinicio** exige rehacer el alta en un entorno de prueba nuevo o usar el tenant real. No convertir ese límite del simulador en evidencia de Graph. El worker y la API comparten proceso en esta ejecución local; el estado `ErrorProvision` y su reintento cuando falla el correo están cubiertos por los tests de `ColaboradoresCuentaNuevaTests`.

## Repetir la prueba local

1. Levantar PostgreSQL según [arranque local](01-arranque-local.md) y Mailpit con `docker compose -f docker-compose.dev.yml --profile adm-qa up -d mailpit`. Verificar `http://127.0.0.1:8025`. Los puertos SMTP/UI solo se publican en la propia máquina.
2. Usar una base de datos **aislada de prueba**, con migraciones y bootstrap aplicados, no una base con datos del cliente. Arrancar la API en `Development` y `FakeForLocalDev`; por ejemplo, en el puerto 5005, estableciendo `ConnectionStrings__Postgres` a esa base. El `appsettings.Development.json` habilita el trabajador simulado y Mailpit en `127.0.0.1:1025`.
3. Arrancar Vite con `VITE_API_BASE_URL=http://127.0.0.1:5005 npm run dev -- --host localhost`. Ingresar como **Super Admin (Dev)**. En **Empleados**, crear el colaborador y revisar la pestaña **Acceso**. En **Cuentas de acceso**, abrir su cuenta y revisar vínculo, rol, sucursal y estado. El correo de prueba estará solo en Mailpit.
4. Con `curl`, `jq` y `uuidgen` instalados, ejecutar `./tools/smoke-adm-local.sh` desde la raíz. Crea registros ficticios con claves QA nuevas, valida provisión/correo/acceso y muestra IDs para inspección. La prueba distingue el correo nuevo por ID, exige asunto, destinatario y contenido esperados; no imprime la clave. No ejecutarlo contra otra API ni entorno.

Si Mailpit no está levantado, el envío falla y el flujo no debe marcarse como notificado; revisar el estado en **Acceso** y reintentar después de levantar el buzón. No usar la contraseña capturada para afirmar que Microsoft autenticó al usuario.

## Solicitud puntual a TI de Millet, para preparar la siguiente etapa

No pedir todavía un servidor Azure para probar esta UI en local. Solicitar: (a) responsable de TI con capacidad de registrar una aplicación **single-tenant** en el tenant existente de Millet y aprobar permisos; (b) confirmación de dominios corporativos, política de MFA/contraseña y una cuenta de prueba autorizada; (c) buzón Exchange de prueba/remitente y correo personal de prueba controlado para verificar recepción; (d) acuerdo de dónde se guardarán secretos de aplicación. Tenant ID y Client ID no son secretos; los secretos nunca se envían por chat ni se guardan en Git. Suscripción/hosting Azure se define aparte para el ambiente donde se desplegará el ERP, no para esta prueba local.

## Criterio de salida de este corte

Build y pruebas locales en verde; bandeja y detalle muestran el vínculo verdadero persistido; alta y gestión de acceso no rompen permisos ni roles/sucursales; QA registra los escenarios probados. No marcar ADM-01/02 como aceptadas por Millet hasta completar validación funcional y la integración real acordada con TI.
