# ADM-01/02 — entrega local para pruebas (corte 2026-09-23)

Rama: `fix/cierre-adm01-adm02-seguridad-flujo`. Este documento describe el corte local; no equivale a QA aprobado, integración con el tenant de Millet, UAT, merge ni despliegue.

## Evidencia de construcción local

- Backend: compilación correcta sin errores ni advertencias; 472/472 pruebas de integración API en la base de prueba aislada `millet_adm_cierre_test_20260923`.
- Frontend: build de producción correcto; 1,589/1,589 pruebas en 288 archivos.
- La prueba de integración de `Dar_Acceso_A_Empleado_Existente_Vincula_Usuario_Rol_Y_Sucursal` comprueba `empleadoId` en listado y detalle; la prueba de UI distingue cuenta vinculada de cuenta sin vínculo.
- No se ejecutó QA manual en navegador ni prueba de recepción de correo. Estas cifras no son aceptación del cliente.

## Qué debe poder revisar el equipo mañana

1. En **Empleados**, dar de alta un colaborador sin acceso, con cuenta Microsoft ya existente en el simulador o con cuenta nueva pendiente de provisión. Comprobar que sucursal, departamento, puesto y rol se guardan en el flujo correspondiente.
2. En **Cuentas de acceso**, localizar el usuario creado y verificar la marca **Empleado vinculado**. Las cuentas históricas sin vínculo se muestran como **Sin empleado vinculado**; no se clasifican automáticamente como cuentas técnicas.
3. Abrir la cuenta y comprobar las asignaciones y el estado activo/inactivo. En el empleado, abrir la sección **Acceso** y probar las acciones permitidas (dar acceso, reintentar, reenviar o reactivar según estado y permiso).
4. Confirmar que una baja laboral bloquea el usuario y que la recontratación no reactiva el acceso automáticamente. Validar que una persona sin permiso no ve ni ejecuta las acciones restringidas.
5. Ejecutar pruebas de regresión y anotar resultado, entorno, caso fallido y evidencia. El reporte A–G es solo de diagnóstico; cualquier corrección B/E/F/G requiere revisión y decisión humana.

## Límite de la simulación

El proveedor `Simulado` permite comprobar consulta y alta con cuentas existentes. Por diseño, la provisión de una cuenta nueva no informa falsamente que un correo fue entregado: el worker está desactivado por defecto y el correo simulado rechaza el envío. Los tests automatizados cubren el contrato del worker con dobles controlados. La recepción real del correo, el cambio de contraseña en Microsoft y el primer acceso con OID real requieren el tenant y el buzón de prueba del cliente; no son criterio de cierre del **corte local**.

## Solicitud puntual a TI de Millet, para preparar la siguiente etapa

No pedir todavía un servidor Azure para probar esta UI en local. Solicitar: (a) responsable de TI con capacidad de registrar una aplicación **single-tenant** en el tenant existente de Millet y aprobar permisos; (b) confirmación de dominios corporativos, política de MFA/contraseña y una cuenta de prueba autorizada; (c) buzón Exchange de prueba/remitente y correo personal de prueba controlado para verificar recepción; (d) acuerdo de dónde se guardarán secretos de aplicación. Tenant ID y Client ID no son secretos; los secretos nunca se envían por chat ni se guardan en Git. Suscripción/hosting Azure se define aparte para el ambiente donde se desplegará el ERP, no para esta prueba local.

## Criterio de salida de este corte

Build y pruebas locales en verde; bandeja y detalle muestran el vínculo verdadero persistido; alta y gestión de acceso no rompen permisos ni roles/sucursales; QA registra los escenarios probados. No marcar ADM-01/02 como aceptadas por Millet hasta completar validación funcional y la integración real acordada con TI.
