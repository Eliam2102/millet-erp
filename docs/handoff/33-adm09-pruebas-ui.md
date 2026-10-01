# ADM-09 — guía de pruebas manuales en la interfaz

Fecha: 1-oct-2026. Rama `feature/F1-ADM-09`. Estado: **implementación parcial**; no declara cierre, aceptación ni producción lista. P01–P06 siguen abiertas.

No escribir en esta guía, en capturas ni en el repositorio: API keys, TID, CSD, contraseñas ni payloads sensibles.

## Prerrequisitos

- PostgreSQL de Docker Desktop (`millet-dev-postgres`) con las 12 migraciones aplicadas (`tools/migration-contexts.txt`).
- Backend en `http://localhost:5000` (`/health/ready` = `Healthy`) y frontend en `http://localhost:5173`.
- `IntegracionesFiscal:Sdk:Disabled=false` y `IntegracionesFiscal:Sdk:TenantKey` presentes en user-secrets de `Millet.Api` (el `TenantKey` es el TID de FiscalAPI, distinto del tenant de EntraID). Reiniciar el backend tras cambiarlos.
- Un usuario con permisos de administración de Integraciones Fiscal y de Series. Para los casos de alcance, un segundo usuario operativo (solo sucursales asignadas) y uno corporativo (permiso de bypass).

## A. Integraciones Fiscal — `/admin/integraciones/fiscal`

| # | Acción | Resultado esperado |
|---|---|---|
| A1 | Abrir la pantalla con una empresa que ya tiene configuración guardada. | CSD «configurado» con su estado de vigencia (Vigente / Próximo a vencer / Vencido). Los campos API key, certificado, llave y contraseña aparecen vacíos: es intencional, no pérdida de secretos. |
| A2 | Con la configuración sin cambios, pulsar **Probar conexión**. | Botón habilitado. Resultado de conexión exitosa (`ms`) o un mensaje que indica la causa real. Ya no debe aparecer un «timeout» con HTTP 0 inmediato. |
| A3 | Con `Sdk:Disabled=true` (o sin `TenantKey`), pulsar **Probar conexión**. | Toast «La integración fiscal no está habilitada en este ambiente. Contacta a TI.» con HTTP 501. Sin detalles internos ni secretos. |
| A4 | Cambiar cualquier campo (por ejemplo «Activo») y **no** guardar. | **Probar conexión** se deshabilita (la prueba usa lo persistido). |
| A5 | Cambiar un campo y **Guardar**. | Toast «Configuración guardada.» y **Probar conexión** vuelve a habilitarse sin recargar la página (regresión de `form.reset` tras guardar). |
| A6 | Escribir una API key nueva sin guardar. | **Probar conexión** deshabilitado hasta guardar. |
| A7 | Guardar dejando vacíos API key y CSD. | Los secretos persistidos se conservan (la siguiente prueba de conexión sigue funcionando). |
| A8 | Escribir en Base URL `http://test.fiscalapi.com`, `https://localhost`, `https://test.fiscalapi.com/x` o `https://test.fiscalapi.com:8443` y guardar. | Rechazo con `CONFIG_PAC_BASE_URL_INVALIDA`; no se guarda. Solo se aceptan `https://test.fiscalapi.com` y `https://live.fiscalapi.com`. |
| A9 | Abrir la misma configuración en dos pestañas. Guardar en la primera y luego en la segunda. | La segunda recibe `409 CONCURRENCY_CONFLICT` y no sobrescribe la rotación de la primera. |
| A10 | Cargar un CSD con certificado y llave que no corresponden, o con contraseña incorrecta. | Error de validación; la configuración y los secretos anteriores no cambian. |
| A11 | Cargar solo una o dos de las tres piezas del CSD. | Mensaje «El CSD requiere las tres piezas…». |

## B. Series y folios — `/admin/series`

| # | Acción | Resultado esperado |
|---|---|---|
| B1 | Listar series con el usuario operativo. | Solo series de sus sucursales asignadas y las globales de lectura permitida; con el usuario corporativo, todas las de la empresa actual. |
| B2 | Crear una serie para una sucursal asignada. | Alta correcta. En una sucursal no asignada: `403`. |
| B3 | Crear una serie global con el usuario operativo. | `403` (las series globales requieren permiso corporativo). |
| B4 | Editar prefijo de una serie. | Se guarda y la fila muestra la versión nueva. |
| B5 | Abrir la misma serie en dos pestañas, editarla en una y luego en la otra. | La segunda falla con `409 CONCURRENCY_CONFLICT`; recargar y reintentar funciona. |
| B6 | Desactivar una serie (confirmar el diálogo). | Toast «Serie desactivada». Con versión desactualizada: `409`. |
| B7 | Después de desactivar, intentar usarla desde un flujo de facturación o con la reserva. | Falla con `SERIE_NO_CONFIGURADA`; no consume folio. |
| B8 | Cambiar de empresa (si hay más de una) y listar. | Solo se ven series de la empresa del JWT; IDs de otra empresa responden como inexistentes. |

## C. Revisiones transversales

- Consola del navegador y pestaña Network: ninguna respuesta contiene API key, certificado, llave o contraseña.
- Logs del backend (`api.log` / consola): sin secretos en los errores de prueba de conexión.
- Registrar para cada caso: fecha/hora, usuario, empresa, resultado obtenido, y la evidencia anonimizada. Los casos que dependen de P02–P05 (unicidad de serie activa, fallback global, continuidad de folios) **no** se consideran aceptados aunque pasen.

## D. Fuera de esta guía

- Emisión sandbox real con UUID/CFDI almacenado (T12) y validación de Fiscal/Contabilidad.
- Decisiones P01–P06 y datos reales de Millet (matriz de series, folios iniciales, lugar de expedición).

Ver `docs/handoff/30-evidencia-f1-adm-09.md`, `docs/handoff/32-adm09-continuacion-sandbox.md` y `docs/modulos/integraciones-fiscal/04-adm09-especificacion-v2-brecha.md`.
