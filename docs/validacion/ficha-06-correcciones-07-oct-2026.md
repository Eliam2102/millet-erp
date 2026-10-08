# Ficha 06 · Correcciones de la prueba del 07-oct-2026

Trabajo local en `fix/correcciones-pruebas-07oct`. Sin commits, push, servidores, cambios en `infra/` ni archivos `.env*`.

## Resultado y decisiones

1. **Emisión y aviso de timbrado.** El endpoint ya conservaba `TimbradoFallido`, UUID nulo y la bitácora, pero su DTO omitía el error del PAC. El formulario usaba `toast.success` para cualquier HTTP 201. La respuesta ahora incluye `timbradoErrorCodigo` y `timbradoErrorMensaje`. Se conserva HTTP 201 porque el comprobante sí se creó: fallar el timbrado no debe provocar una segunda factura. El aviso solo anuncia éxito con estado `Timbrado` y UUID no vacío; muestra error y código/mensaje del PAC para `TimbradoFallido`; los estados pendientes generan advertencia. El acceso «Reintentar timbrado» abre el detalle del comprobante, donde se reutiliza la acción existente, sus permisos y confirmación de no duplicidad. No ejecuta otro intento automáticamente.
2. **FIEL en lugar de CSD.** El guardado reutiliza `CsdValidador` y rechaza certificados sin OU no vacía con código `CONFIG_PAC_CSD_ES_FIEL` y texto «Este archivo es una e.firma (FIEL), no un sello digital (CSD)». Se lee el OID ASN.1 `2.5.4.11`, evitando confundir una cadena `OU=` dentro del CN con el atributo real. Conserva validaciones de contraseña, correspondencia de llave y vigencia. Se valida también una carga cuyo hash coincida con el material anterior. No se altera el flujo separado de FIEL para descarga SAT. La regla OU identifica el tipo esperado; no acredita cadena de confianza, vigencia en LCO ni aceptación del PAC.
3. **Cambio de pedido.** `FormInner` se monta con `key` del pedido. Cambiar de pedido reinicia valores del formulario, cliente, anticipos, autorización y clave de idempotencia. La prueba cambia de PED-1002/obra Torre Cancún/total $100,920 a PED-1001/otra obra/$4,640 y verifica que desaparezcan los valores anteriores.
4. **Subtotal OC.** Reproducción con datos sintéticos: 10 × $10 sin descuento = $100; con descuento de $10 o 10 % = $90. El cálculo del dominio es correcto. Se encontraron tres defectos de presentación/captura: el DTO omitía tipo/valor del descuento, la fila no lo explicaba y la vista previa mostraba el bruto; además, editar inicializaba descuento en vacío. El DTO y Mapster ahora exponen tipo/valor, la fila muestra «Sin descuento» o el descuento en monto/porcentaje y el editor conserva esos valores. La vista previa aplica descuento y redondeo a centavos al par; el subtotal persistido sigue viniendo del backend. Si una respuesta antigua omite el descuento, la fila dice «Descuento no informado». **El descuento exacto de la OC observada en la demo queda Por confirmar:** no se consultó su base de datos.
5. **Mensajes y navegación.** Se realizó la búsqueda estática indicada abajo. No se modificaron los menús ni los textos que pertenecen a la ficha 05.

Los eventos de integración mantienen su publicación antes de `SaveChanges`; no se modificaron estados de negocio, migraciones ni contratos Outbox.

## Mensajes revisados contra menús

Se buscaron indicaciones de navegación (`→`, `Ir a`, `Ve a`, `Captúralo en`, `Configúrala`, referencias a Administración/Configuración/catálogos) en `frontend/src` y `backend/src`. Se contrastaron con `lib/nav.ts`, `lib/admin/registry.ts`, los registros `admin.ts`, enlaces de pantallas y `QuickCreateMenu.tsx`. La visibilidad final sigue dependiendo de permisos.

| Mensaje / origen | Destino comprobado en código | Hallazgo |
|---|---|---|
| «Captúralo en Administración → Empresas» · `DatosFiscalesEmisor.cs:40` | `/admin/empresas/$id`, accesible desde Sucursales → Datos de la empresa y desde Departamentos | **Sin entrada directa de menú.** Es el caso de la ficha 05; la instrucción no describe la ruta visible. |
| «Configuración → Vehículos y operadores» · `OperadorPicker.tsx:54` y `VehiculoPicker.tsx:59` | `/admin/carta-porte-catalogos`, registro `features/facturacion/admin.ts` | Pantalla con entrada en Administración. Dos mensajes con ubicación imprecisa; no son pantallas ocultas. |
| «Integraciones Fiscal → Configuración del PAC» · `FiscalApiTimbradoAdapter.cs:68` | Administración → Integraciones Fiscal, `/admin/integraciones/fiscal` | Entrada existente; la configuración está dentro del detalle de empresa. |
| «Administración → Series» · `ReservarFolioCommand.cs:131` | Administración → Series y folios, `/admin/series` | Entrada existente; nombre abreviado en el mensaje. |
| «Datos Maestros → Clientes» · `DetallePedido`, `NuevoAnticipo`, `EmitirFacturaForm`, `ReceptorFiscalInfo` | `/admin/datos-maestros/clientes`; hay enlaces directos al cliente cuando se dispone del permiso | Entrada existente en Administración. |
| «Datos Maestros → Productos A+W» · `TabPosiciones.tsx:94` | `/admin/datos-maestros/productos-aw` y enlace al producto | Entrada existente en Administración. |
| «Administración → Empleados» · `TransicionesViaticosCommands.cs:107` y «Ir a Empleados» · `UsuariosLayout.tsx:70` | `/admin/empleados` | Entrada existente. |
| «Compras → Configuración → Aprobadores» · `features/compras/pages/Ayuda.tsx:313` | `/compras/admin/aprobadores` | Entrada existente en menú de Compras. |
| «Ve a la pestaña Departamentos» · `SucursalPuestosTab.tsx:722` | Pestaña del detalle de sucursal | Acceso contextual existente. |
| «Quick Create → Anticipo» · `BandejaFacturasAnticipo.tsx:160` | Acción Anticipo de `QuickCreateMenu.tsx:90` | Acción existente, filtrada por permiso. |

En esta búsqueda no se detectaron otras indicaciones hacia una pantalla sin entrada de menú, además del caso Empresas ya asignado a ficha 05. Esto es evidencia de código; no una prueba de navegación con todos los perfiles reales.

## Pruebas añadidas o ampliadas

- Emisión: respuesta del handler con UUID/error; función pura para éxito, UUID vacío, 403, 400, 305 y estados pendientes; componente con POST 201 fallido, mensaje real y acceso al reintento.
- Cambio de pedido: prueba de componente con ambos totales y obras, sin desmontar el formulario padre.
- Certificados: material RSA/X.509 generado durante las pruebas con OU, sin OU, OU vacía y `OU=` dentro del CN; rechazo antes de abrir una llave con contraseña equivocada; guardado rechazado sin persistencia ni eventos. Se conserva la cobertura anterior de contraseña, par y vigencia. El certificado público se firma sin asociarle la llave privada, evitando depender del llavero de macOS.
- OC: dominio + Mapster para 10 × $10 con/sin descuento; función pura de subtotal, límite del descuento y redondeo; fila con explicación visible; edición que conserva el descuento en el PATCH.
- Integración escrita: POST factura con PAC 403/400, persistencia del error/bitácora y replay idempotente; PUT configuración con FIEL rechazado y configuración anterior intacta; POST/GET línea OC con tipo, valor y subtotal. Estas pruebas usan PostgreSQL desechable y datos de catálogos del seed.

## Certificados públicos SAT

No se encontraron archivos `.cer`, `.crt` o `.pem` en los archivos del repositorio. Se consultó la página oficial del SAT [Servicios especializados de validación](https://wwwmat.sat.gob.mx/consultas/20585/conoce-los-servicios-especializados-de-validacion), que enlaza «Certificados de Prueba» y distingue el CSD de la e.firma para facturación con PAC. El enlace de descarga entrega ZIP, que el navegador de investigación no pudo extraer; `curl` falló con `Could not resolve host: wwwmat.sat.gob.mx` por la red del sandbox. **La inspección de OU en ese paquete público queda pendiente**. Las pruebas usan certificados generados, según la alternativa autorizada en la ficha.

## Validación y siguiente acción

| Verificación | Resultado final |
|---|---|
| `dotnet build Millet.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` | **0 errores, 0 advertencias**, 16.84 s. Incluye compilación de las pruebas de integración nuevas. Se restauró primero desde caché con `NuGetAudit=false` porque el sandbox no permite consultar NuGet. Compilación serial sin servidor compartido para evitar los bloqueos del entorno. |
| Unitarias de Facturación | **342 aprobadas, 0 fallidas, 0 omitidas**. |
| Unitarias de Integraciones Fiscal | **204 aprobadas, 0 fallidas, 0 omitidas**. |
| Unitarias de Compras | **536 aprobadas, 0 fallidas, 0 omitidas**. |
| `npx tsc --noEmit -p tsconfig.json` | Exit 0. |
| `npm run -s typecheck:test` | Exit 0. |
| `npm run -s lint` | Exit 0: **0 errores y 10 advertencias preexistentes** de dependencias de hooks. |
| `npx vitest run` | **333 archivos y 1,960 pruebas aprobadas**, 112.46 s. Avisos de jsdom sobre canvas/navegación; no fallos. |
| `git diff --check` | Exit 0. |

**Cómo se ejecutaron las unitarias .NET:** el comando estándar `dotnet test` se anuló antes de ejecutar casos porque VSTest intenta abrir un socket (`SocketException (13): Permission denied`). Se utilizó `XunitFrontController` del runner oficial xUnit disponible en la caché, con descubrimiento y ejecución de todas las pruebas de los tres assemblies, sin filtros ni casos omitidos y sin sockets. El ejecutable temporal se corrió desde la carpeta `bin` de Compras para respetar las auditorías que localizan el código por rutas relativas. Las fuentes del runner quedan en `/private/tmp/millet-ficha06-xunit`; no se incorporó una herramienta nueva al repositorio. **El comando VSTest convencional sigue pendiente de ejecución fuera del sandbox.**

Logs de esta sesión (temporales): `/tmp/millet-ficha06-build-entrega.log`, `/tmp/millet-ficha06-xunit-final.log`, `/tmp/millet-ficha06-vitest-final.log`, `/tmp/millet-ficha06-lint-final.log`.

**Sin verificar:** ejecución de integración PostgreSQL y su rojo/verde; timbrado real con FiscalAPI; certificados del ZIP público del SAT; UI en navegador y datos exactos de la OC observada. No se declara aceptación en vivo.

Claude debe ejecutar `tools/validate-integration-isolated.sh` completo, comprobar rojo/verde de las regresiones con PostgreSQL desechable y repetir el recorrido visible de la demo. No se ejecutaron pruebas contra FiscalAPI/SAT real ni una sesión de navegador de la aplicación.

Commit propuesto (no realizado):

`fix(demo): corregir timbrado, CSD, pedidos y descuentos de OC`

## Archivos modificados o añadidos

- `backend/src/Compras/Application/Oc/ObtenerOrdenCompraPorId/LineaOrdenCompraResponse.cs`
- `backend/src/Compras/Application/Oc/OcMapsterConfig.cs`
- `backend/src/Facturacion/Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaCommand.cs`
- `backend/src/Facturacion/Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaHandler.cs`
- `backend/src/Integraciones.Fiscal/Application/Configuracion/GuardarConfiguracionPac/GuardarConfiguracionPacHandler.cs`
- `backend/src/Integraciones.Fiscal/Infrastructure/Cifrado/CsdValidador.cs`
- `backend/tests/Api.IntegrationTests/Compras/Oc/OrdenesCompraEndpointsTests.cs`
- `backend/tests/Api.IntegrationTests/Facturacion/EmisionResultadoEndpointsTests.cs`
- `backend/tests/Api.IntegrationTests/IntegracionesFiscal/ConfiguracionPacEndpointsTests.cs`
- `backend/tests/Compras.UnitTests/Oc/Domain/LineaOrdenCompraTests.cs`
- `backend/tests/Facturacion.UnitTests/Facturas/EmitirFacturaVentaHandlerTests.cs`
- `backend/tests/Integraciones.Fiscal.UnitTests/Application/GuardarConfiguracionPacHandlerTests.cs`
- `backend/tests/Integraciones.Fiscal.UnitTests/Infrastructure/CsdValidadorTests.cs`
- `docs/validacion/ficha-06-correcciones-07-oct-2026.md`
- `frontend/src/features/compras/ordenes/api/types.ts`
- `frontend/src/features/compras/ordenes/components/EditorLineas.cc.test.tsx`
- `frontend/src/features/compras/ordenes/components/EditorLineas.tsx`
- `frontend/src/features/compras/ordenes/components/LineaInlineFormOc.cc.test.tsx`
- `frontend/src/features/compras/ordenes/components/LineaInlineFormOc.tsx`
- `frontend/src/features/compras/ordenes/lib/subtotal-linea.test.ts`
- `frontend/src/features/compras/ordenes/lib/subtotal-linea.ts`
- `frontend/src/features/facturacion/api/types.ts`
- `frontend/src/features/facturacion/components/emitir-factura/EmitirFacturaForm.smoke.test.tsx`
- `frontend/src/features/facturacion/components/emitir-factura/EmitirFacturaForm.tsx`
- `frontend/src/features/facturacion/components/emitir-factura/resultado-emision.test.ts`
- `frontend/src/features/facturacion/components/emitir-factura/resultado-emision.ts`
