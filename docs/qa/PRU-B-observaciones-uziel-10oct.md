# PRU-B · Observaciones de Uziel del 10 de octubre

Fecha de trabajo: 11 de octubre de 2026. Worktree: `millet_erp-PRU-B`. Rama: `fix/PRU-B-observaciones-prueba-uziel`.

Estado: implementación local y pruebas de interfaz verificadas. La aceptación de backend, integración y prueba en vivo queda **Por confirmar**. No se hizo commit, push, despliegue ni modificación de datos de una base en operación. No se usó ni activó el login simulado, ni se levantaron servidores. Las nuevas pruebas HTTP reutilizan las fixtures aisladas existentes; se escribieron y compilaron, pero no se ejecutaron aquí.

## Resultado breve por observación

| Punto | Resultado local y decisión |
|---|---|
| 1 · CLABE | El código actual ya enmascara la CLABE del proveedor salvo el permiso de Tesorería para ver la cuenta completa. Se conserva la regla y se añade regresión con/sin permiso. La identidad y los permisos de la sesión de Uziel son Por confirmar; no se atribuye una fuga al código actual sin esa evidencia. |
| 2 · Nota de cargo | Dominio rechaza a quien la creó y a un actor sin identidad. Botón Autorizar en el detalle, deshabilitado para quien capturó; avisos dicen «nota de cargo». Actualizadas las fixtures antiguas que autorizaban cargos con usuario nulo. |
| 3 · Beneficiario de crédito | Selector dedicado de usuarios activos de la empresa con permiso efectivo para decidir liberaciones de Crédito y Cobranza. El servidor vuelve a verificar el permiso al crear la autorización. Usa el cargador canónico de roles, grupos y excepciones. |
| 4 · Roles por empresa | El modelo admite varios roles: unicidad por usuario + empresa + rol y unión de permisos. Se conserva, conforme a la alternativa explícita de la ficha. La frase «rol único» de CLAUDE.md no describe el dominio actual. Combos etiquetados y roles con nombre, sin concatenar código. |
| 5 · Orden directa | El editor de borrador ya permite agregar, editar y quitar líneas manuales de una orden marcada sin requisición. Se evita crear desde la pantalla una orden ordinaria sin requisiciones que dejaba oculto ese editor: exige requisición o marcar la orden directa con motivo/evidencia. Se conserva la cotización obligatoria. |
| 6 · Proveedor en revisión | Acción con motivo de 5–500 caracteres y permiso de validación de proveedores de Cuentas por pagar. Cambia Activo a En revisión y registra motivo en bitácora. Los handlers vigentes de creación de órdenes ya rechazan proveedores en revisión; se añade prueba HTTP de ese rechazo. |
| 7 · Área revisora | Selector del catálogo real de departamentos activos de la empresa, tanto al enviar como al consultar revisión. Puerto productivo y consulta CQRS; eliminado el stub de cuatro identificadores. |
| 8 · Centro de costo | La herencia vigente desde departamento ya existía. La semilla DEMO elegía áreas por posición, sin relación con su nombre. Ahora exige coincidencia única con el departamento; corrige únicamente equivalencias con la marca exacta del seed anterior. Conserva equivalencias mantenidas manualmente. Si no hay coincidencia única, elimina la propuesta DEMO incorrecta y deja la equivalencia Por confirmar. |
| 9 · Motivos de Compras | Las acciones usan un helper común que prioriza el detalle del servidor. El diálogo muestra el motivo al rechazar cotización o autoautorización, en lugar de «La acción falló». |
| 10 · Aviso de Presupuesto/Almacén | Se quitó el código de seguimiento del aviso de asignación; se muestra el motivo. |
| 11 · Identificadores internos | Bandeja de órdenes recibe el nombre del comprador mediante lectura en lote y usa nombre pendiente si falta. El detalle de factura de proveedor ya tenía folio de orden con enlace en main; se conserva y se verifica su prueba existente. La falta de serie de anticipo explica configurar FANT en Administración → Series, sin empresa/sucursal como identificadores. |
| 12 · Cartera | Valores booleanos del reporte se presentan y exportan como Sí/No. |
| 13 · RFC de cliente incompleto | En el código actual el renglón RFC ya toma `rfc`, separado de razón social. La falla histórica no se reprodujo en esta versión. Se añade prueba del cliente sin RFC y se cambia «master» por «catálogo» en el aviso. |
| 14 · Cajas | Avisos completos: «Se actualizaron las sucursales de la caja» y «Se actualizaron los cajeros de la caja». |
| 15 · Proveedor desde CFDI | Se reutiliza el autollenado existente por RFC emisor. Una respuesta tardía no sobrescribe la selección manual ni la de otro CFDI; conserva el proveedor al cambiar campos. Se mantiene la elección manual cuando el RFC no tiene coincidencia única. |
| 16 · Factura origen | Selector de notas de cargo excluye pagadas, canceladas y facturas sin saldo. No altera los otros usos del selector. |
| 17 · Trazabilidad | Fecha de factura de proveedor se trata como fecha de calendario, sin restar un día por zona horaria. Documentos compartidos se expanden una sola vez en el árbol, evitando repetir la orden en cada rama. |
| 18 · Folio XML | El adapter envía al PAC el folio reservado por el ERP en `number`. La documentación oficial de FiscalAPI confirma que admite folio propio desde julio de 2026. Comparación del XML realmente timbrado contra el folio del ERP: Por confirmar. |
| 19 · Hora de timbrado | Convierte la fecha local devuelta por el PAC a UTC al persistir; los detalles de factura y anticipo muestran la hora de México explícitamente. No se migraron fechas históricas ya guardadas; contrastarlas con el XML antes de cualquier corrección. |
| 20 · Hueco de folio | No se modifica la numeración ni se recuperan folios consumidos por intentos fallidos. El reintento actual reconstruye la emisión con `comprobante.Folio`, sin reservar otro. No se editó el reintento de PRU-A; su resultado real queda Por confirmar. Una emisión nueva seguirá reservando un nuevo folio. |
| 21 · Unidad SAT DEMO | Reglas Development para Producto, Vidrio plano, VTE, VLA y VC usan MTK. Proceso conserva E48; Relleno y Perfil intercalario conservan H87. Solo configuración DEMO, sin cambio de negocio ni modificación masiva de fichas fiscales existentes. |
| 22 · Puestos DEMO | Seed completa un puesto activo por departamento asignado activo en MID, MTY y QRO. Conserva asignaciones existentes, incluidas las inactivas; si necesita otra, usa una clave libre y un identificador nuevo. La prueba de dos arranques verifica cobertura e idempotencia en una base propia. Ejecución del seed: Por confirmar. |

## Pruebas y evidencia

Para los puntos 1–8 se añadieron pruebas de regresión de: CLABE con/sin permiso; autoautorización del cargo y HTTP con actor distinto; beneficiarios permitidos/no permitidos; dos roles en una empresa; controles de agregar/editar/quitar líneas directas; transición de proveedor, motivo en auditoría y bloqueo HTTP de nueva orden; departamentos reales activos; correspondencia de nombres de departamento y área. Las pruebas .NET están compiladas, con ejecución pendiente por la restricción del sandbox.

Vitest cubre los puntos 9, 11, 12, 15 y 16: rechazos de Compras; comprador con nombre y folio de orden con enlace; aviso de FANT; cartera Sí/No; autollenado por RFC y carrera con selección manual; factura origen con saldo. Además hay regresiones de fecha de calendario y RFC faltante. Se añadió prueba backend del árbol con dos facturas que comparten orden.

| Validación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore --disable-build-servers -p:UseSharedCompilation=false -m:1` | 0 errores, 0 advertencias. Restore previo desde caché local con auditoría NuGet deshabilitada para evitar la red restringida; no se cambiaron dependencias. |
| `npx tsc --noEmit -p tsconfig.json` | Correcto. |
| `npm run -s typecheck:test` | Correcto. |
| `npm run -s lint` | 0 errores; 10 advertencias en archivos de Administración ajenos a los cambios. |
| `npx vitest run` | 381 archivos y 2,158 pruebas en verde. Duración de la ejecución final: 95.89 s. Hay mensajes conocidos de jsdom sobre canvas y navegación; ninguna prueba falló. |
| Unitarias .NET: CxP, CxC, Compras, Compartido, Identidad, Tesorería, Integraciones.Fiscal y Api | Ocho intentos anulados antes de ejecutar pruebas: `System.Net.Sockets.SocketException (13): Permission denied`, al abrir el socket local de VSTest. No equivale a pruebas en verde ni a fallas de reglas de negocio. |
| Integración PostgreSQL | Nuevas pruebas escritas y compiladas. No ejecutadas: este sandbox no tiene Docker. Claude debe correr `tools/validate-integration-isolated.sh` completo y el rojo/verde. |
| `git diff --check` | Correcto. |

## Pendientes y siguiente acción

1. Claude: ejecutar las unitarias .NET y la puerta completa de integración en PostgreSQL desechable; confirmar rojo/verde de las regresiones. No usar una base compartida de operación.
2. Repetir con Millet los casos de Uziel mediante autenticación real: permisos bancarios, segregación de cargo y beneficiarios, orden directa con cotización, proveedor en revisión y selección de departamento.
3. Confirmar equivalencia real de Mantenimiento cuando el catálogo no ofrezca coincidencia única. El seed no inventa una relación para satisfacer la pantalla.
4. Timbrar una factura de prueba y comparar `Folio` y fecha del timbre en su XML contra el ERP; ejecutar el reintento preparado por PRU-A y confirmar reutilización del folio. Revisar fechas históricas por separado.
5. No aprobar cierre, integración real ni aceptación funcional a partir de la compilación o de Vitest.

Fuentes oficiales revisadas: [folio personalizado de FiscalAPI](https://fiscalapi.com/blog/folio-de-factura-personalizado) y [emisión de factura y respuesta de timbrado](https://docs.fiscalapi.com/create-invoice-tutorial). La interpretación de fecha local se aplica al formato sin zona que entrega el adapter actual; falta contrastarla con la respuesta real y su XML.

Mensaje de commit propuesto, **sin ejecutar**: `fix(pru-b): corregir observaciones de la prueba de Uziel`.

La actualización de la base Obsidian se deja preparada en `PRU-B-actualizacion-base-borrador.md`: el sandbox permite escribir en este worktree, pero no en la bóveda externa. No se afirma que la base esté actualizada.

## Anexo de archivos cambiados


### Backend: código y configuración

- `backend/src/Api/Endpoints/Catalogos/CatalogosEndpoints.cs`
- `backend/src/Api/Endpoints/CuentasPorCobrar/LiberacionesEndpoints.cs`
- `backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpEndpoints.cs`
- `backend/src/Api/Seed/DemoSesionMaestros.cs`
- `backend/src/Api/appsettings.Development.json`
- `backend/src/CentrosCosto/Infrastructure/Seed/ADM08EquivalenciasDemoHostedService.cs`
- `backend/src/Compartido/Application/Administracion/Series/ReservarFolioCommand.cs`
- `backend/src/Compartido/Application/Catalogos/Proveedores/PonerProveedorEnRevisionCommand.cs`
- `backend/src/Compras/Application/Oc/ListarOrdenesCompra/ListarOrdenesCompraHandler.cs`
- `backend/src/Compras/Application/Oc/ListarOrdenesCompra/ListarOrdenesCompraQuery.cs`
- `backend/src/Compras/Infrastructure/Trazabilidad/ObtenerArbolDocumentosService.cs`
- `backend/src/CuentasPorCobrar/Application/Autorizaciones/AutorizacionesCreditoCommands.cs`
- `backend/src/CuentasPorCobrar/Domain/Ports/DatosMaestros/IBeneficiarioCreditoReadPort.cs`
- `backend/src/CuentasPorCobrar/Infrastructure/Adapters/BeneficiarioCreditoReadPortAdapter.cs`
- `backend/src/CuentasPorCobrar/Infrastructure/DependencyInjection.cs`
- `backend/src/CuentasPorPagar/Application/Catalogos/ListarDependenciasRevisorasQuery.cs`
- `backend/src/CuentasPorPagar/Domain/NotaCargo/NotaCargo.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Adapters/DependenciaRevisoraReadPortAdapter.cs`
- `backend/src/CuentasPorPagar/Infrastructure/DependencyInjection.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Stubs/NoOpDependenciaRevisoraReadPort.cs`
- `backend/src/DatosMaestros/Domain/Proveedor.cs`
- `backend/src/Integraciones.Fiscal/Infrastructure/SdkAdapter/FiscalApiTimbradoAdapter.cs`

### Backend: pruebas

- `backend/tests/Api.IntegrationTests/Administracion/DemoSesionSeedTests.cs`
- `backend/tests/Api.IntegrationTests/CuentasPorPagar/P4EndpointsTests.cs`
- `backend/tests/Api.IntegrationTests/CuentasPorPagar/P8EndpointsTests.cs`
- `backend/tests/Api.IntegrationTests/CuentasPorPagar/PrubObservacionesEndpointsTests.cs`
- `backend/tests/Api.UnitTests/DemoSesionSeedTests.cs`
- `backend/tests/Compartido.UnitTests/Proveedores/ValidacionProveedoresHandlersTests.cs`
- `backend/tests/Compras.UnitTests/PublicAdapters/ArbolDocumentosPrubTests.cs`
- `backend/tests/CuentasPorCobrar.UnitTests/Liberacion/DecidirLiberacionHandlerTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/Adapters/PuestoEmpleadoReadAdaptersTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/NotaCargo/NotaCargoAggregateTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/NotaCargo/NotaCargoSucursalNullableTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/P4/SaldosP4Tests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/P8/AplicacionesP8Tests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/Stubs/NoOpStubsTests.cs`
- `backend/tests/Identidad.UnitTests/Domain/UsuarioEstadoAccesoTests.cs`
- `backend/tests/Integraciones.Fiscal.UnitTests/Infrastructure/FiscalApiTimbradoAdapterTests.cs`
- `backend/tests/Tesoreria.UnitTests/Pagos/P5ReglasHandlersTests.cs`

### Frontend: pantallas, componentes y pruebas

- `frontend/src/components/erp/trazabilidad/NodoDocumento.tsx`
- `frontend/src/components/erp/trazabilidad/fecha-documento.test.ts`
- `frontend/src/components/erp/trazabilidad/fecha-documento.ts`
- `frontend/src/features/almacen/components/AsignacionSheet.tsx`
- `frontend/src/features/compras/ordenes/api/types.ts`
- `frontend/src/features/compras/ordenes/components/AccionesOC.prub.test.tsx`
- `frontend/src/features/compras/ordenes/components/AccionesOC.tsx`
- `frontend/src/features/compras/ordenes/components/EditorLineas.cc.test.tsx`
- `frontend/src/features/compras/ordenes/pages/BandejaOrdenesCompra.smoke.test.tsx`
- `frontend/src/features/compras/ordenes/pages/BandejaOrdenesCompra.tsx`
- `frontend/src/features/compras/ordenes/pages/SheetNuevaOC.tsx`
- `frontend/src/features/cxc/components/NuevaAutorizacionDialog.tsx`
- `frontend/src/features/cxp/api/useRevision.ts`
- `frontend/src/features/cxp/components/DocumentosProveedor.prub.test.tsx`
- `frontend/src/features/cxp/components/EnviarRevisionSheet.tsx`
- `frontend/src/features/cxp/components/FacturaPicker.prub.test.tsx`
- `frontend/src/features/cxp/components/FacturaPicker.tsx`
- `frontend/src/features/cxp/components/NuevaNotaCargoSheet.tsx`
- `frontend/src/features/cxp/components/NuevaNotaCreditoSheet.tsx`
- `frontend/src/features/cxp/components/NuevoAnticipoSheet.tsx`
- `frontend/src/features/cxp/lib/reportes-adapter.ts`
- `frontend/src/features/cxp/pages/NotaCargoDetallePage.tsx`
- `frontend/src/features/cxp/pages/NotasCargoPage.tsx`
- `frontend/src/features/cxp/pages/ReporteCarteraPage.prub.test.tsx`
- `frontend/src/features/cxp/pages/RevisionPage.smoke.test.tsx`
- `frontend/src/features/cxp/pages/RevisionPage.tsx`
- `frontend/src/features/facturacion/components/ErrorTimbradoAviso.prub.test.tsx`
- `frontend/src/features/facturacion/components/ReceptorFiscalInfo.prub.test.tsx`
- `frontend/src/features/facturacion/components/ReceptorFiscalInfo.tsx`
- `frontend/src/features/facturacion/components/emitir-factura/EmitirFacturaForm.smoke.test.tsx`
- `frontend/src/features/facturacion/pages/DetalleCaja.tsx`
- `frontend/src/features/facturacion/pages/DetalleFactura.tsx`
- `frontend/src/features/facturacion/pages/DetalleFacturaAnticipo.tsx`
- `frontend/src/lib/api/error.ts`
- `frontend/src/modules/datos-maestros/components/ProveedorDetalle.tsx`
- `frontend/src/modules/identidad/components/RolesPorEmpresaInlineForm.tsx`

### Documentación local

- `docs/qa/PRU-B-actualizacion-base-borrador.md`
- `docs/qa/PRU-B-observaciones-uziel-10oct.md`
