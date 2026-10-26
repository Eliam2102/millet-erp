# FMT-3 · Continuación del 22 de octubre de 2026

## Estado

Trabajo local en `feature/fmt3-facturacion-datos`, HEAD inicial `7f1714cffce7aa80cbb4be8bef9e3865a8500ec7`. Leídos completos y en orden: continuación, reglas comunes y ficha FMT-3. Base de comparación: `origin/feature/86e3tfdw2-pedidos-credito-notas-anticipos` (PR #191). No se hizo commit, push, cambio de rama ni aplicación de migraciones a una base de datos.

**Implementación y verificación local cerradas. Integración en Git pendiente de marcar tres archivos**, por restricción del sandbox; no se puede declarar cerrada la integración. Se leyó la corrección final del 22-oct: el anticipo conserva «Anticipo de clientes» y cambia sólo su unidad a ACT. La migración se regeneró por EF; no se aplicó a ninguna base de datos.

## Conflictos

Resueltos en el contenido de:

- `backend/src/Facturacion/Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaCommand.cs`: se conserva `Observaciones` de la base y se agregan los cuatro campos de cabecera de FMT-3. Se elimina `Comentarios` de la factura.
- `backend/src/Facturacion/Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaHandler.cs`: se conserva la carga y validación del pedido de la base y el armado compartido `FacturaVentaArmado`. Las medidas se copian antes del armado; no se duplican partidas ni el complemento CCE. Se conservan referencia/peso/área explícitos cuando no los aporta el pedido; se valida su longitud y rango en el comando antes del folio.
- `backend/tests/Facturacion.UnitTests/Facturas/EmitirFacturaVentaHandlerTests.cs`: se conservan ambos dobles de prueba, `sender` de la base y `productos` de FMT-3.

`git add` no pudo marcar las resoluciones: el sandbox denegó crear `millet_erp-review/.git/worktrees/millet_erp-fmt-fact/index.lock`, fuera de las raíces editables. Los tres archivos todavía aparecerán como no fusionados en el índice hasta que Claude ejecute `git add` sobre ellos. No se intentó eludir esta restricción.

## Cambios por archivo

Rutas relativas al worktree; incluye el avance previo conservado.

### Backend

- `Domain/Facturas/FacturaVenta.cs`: cabecera opcional para impresión, edición antes de timbrar y totales de kilos, pies cuadrados y piezas calculados desde partidas. `Observaciones` es el único comentario de la factura.
- `Domain/Facturas/DatosImpresion.cs`: validación de longitud y medidas no negativas, con precisión de tres decimales.
- `Domain/Facturas/FacturaVentaLinea.cs`: referencia, peso y área opcionales de cada partida.
- `Domain/Pedidos/PedidoFacturableLinea.cs`: setters de impresión preparados para futura ingesta.
- `Domain/Ports/IProductosReadPort.cs` e `Infrastructure/DatosMaestros/ProductosReadAdapter.cs`: lectura del peso unitario del maestro mediante el puerto propio de Facturación.
- `Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaCommand.cs`: cabecera e información de impresión por partida transportada al armado común.
- `Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaHandler.cs`: copia del pedido y peso unitario por cantidad cuando falta peso. La condición capturada prevalece; después se toma la del pedido; después «CONTADO» para PUE.
- `Application/Facturas/EmitirFacturaVenta/FacturaVentaArmado.cs`: conserva el armado único de partidas y CCE de la base, incorporando datos de impresión.
- `Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaValidator.cs`: límites de cabecera; conserva validación de observaciones de la base.
- `Application/Facturas/EditarDatosCliente/EditarDatosClienteCommand.cs`: edición con validación, alcance de acceso y versión esperada.
- `Application/Facturas/Queries/ComprobanteDetalleQuery.cs`: cabecera, observaciones, medidas, totales y certificación en el detalle.
- `Application/Pedidos/Queries/PedidoFacturableDetalleQuery.cs`: medidas y referencia del pedido en su detalle.
- `Domain/Comprobantes/CadenaOriginalTimbre.cs`: extracción pura de cadena original y certificado del emisor, con lectura XML segura.
- `Domain/Comprobantes/Comprobante.cs`: persistencia informativa del timbre y rechazo de XML de otro UUID.
- `Application/Timbrado/TimbradoEjecutor.cs`: registra esos datos para todos los tipos de comprobante usando el resultado del PAC.
- `Application/Timbrado/RecalcularDatosTimbre.cs`: mantenimiento explícito por comprobante desde XML archivado; sin endpoint público ni ejecución automática.
- `Domain/Anticipos/FacturaAnticipo.cs`: ACT y descripción predeterminada «Anticipo de clientes», conforme a la corrección final del 22-oct, conservando descripción explícita y captura por total.
- `Domain/NotasCredito/NotaCredito.cs`: ACT para amortización, bonificación y ranura, conservando descripciones.
- `Infrastructure/Persistence/Configurations/ComprobanteConfiguration.cs`: certificado del emisor y cadena del timbre.
- `Infrastructure/Persistence/Configurations/FacturaVentaConfiguration.cs`: cuatro campos de cabecera; observaciones existentes; totales excluidos de persistencia.
- `Infrastructure/Persistence/Configurations/FacturaVentaLineaConfiguration.cs` y `PedidoFacturableLineaConfiguration.cs`: longitudes y precisión decimal 18,3.

Las rutas anteriores parten de `backend/src/Facturacion/`. Fuera del módulo, sólo sus consumidores pedidos por la ficha:

- `backend/src/Api/Endpoints/Facturacion/FacturasEndpoints.cs`: PUT de datos para el cliente con permiso de emisión e `If-Match`.
- `backend/src/Api/Seed/DemoSesionFacturacion.cs`: referencia, peso y área ficticios identificados como DEMO.

### Frontend (`frontend/src/features/facturacion/`)

- `api/types.ts`: contratos de cabecera, observaciones, partidas, totales y certificación.
- `api/useEditarDatosCliente.ts`: guardado con versión e invalidación del detalle.
- `lib/datos-cliente.ts`: cinco campos y límites compartidos.
- `components/DatosClienteCampos.tsx`: bloque «Datos para el cliente» y ayuda sobre exclusión del CFDI.
- `components/DatosClienteFactura.tsx`: edición con permiso y estado; lectura cuando ya se timbró.
- `components/emitir-factura/EmitirFacturaForm.tsx`: envía la cabecera y conserva la nota de anticipos antes de los comentarios del pedido, separados por salto de línea. La edición manual detiene el prellenado automático.
- `components/emitir-factura/TabEncabezado.tsx`: captura de los cinco campos en un bloque.
- `components/emitir-factura/TabTotales.tsx`: retira el segundo editor de observaciones, ahora centralizado en el encabezado; mantiene captura de anticipos.
- `components/emitir-factura/prefill.ts` y `valores.ts`: prellenado de comentarios del pedido en observaciones y condición de pago de origen.
- `schemas/emitir-factura.ts`: límites de los cuatro campos adicionales; conserva el esquema de observaciones.
- `pages/DetalleFactura.tsx`: bloque de cliente, medidas por partida, totales y datos del timbre. Los nuevos bloques quedan después del encabezado, manteniendo el folio y las acciones al inicio.
- `pages/DetallePedido.tsx`: envía referencia, comentarios y condición de pago al formulario.

## Decisiones

- No existe un campo de días de crédito en `DatosMaestros/Domain/Cliente.cs` en esta base. Sin condición del pedido, PUE usa «CONTADO» y PPD queda vacío para captura; no se inventa un plazo ni se modifica Datos Maestros.
- Los totales físicos se calculan desde las partidas, sin columnas redundantes.
- Los datos de impresión no se incorporan al contrato fiscal de `CfdiEmisionBuilder`; las unidades de productos y transporte conservan su significado.
- El mantenimiento del timbre sólo existe como comando interno. No modifica XML histórico ni reemite comprobantes.
- Las observaciones explícitas son la edición final del operador. El formulario combina anticipos y comentarios al prellenar, sin concatenar nuevamente en el servidor.

## Pruebas

- `backend/tests/Facturacion.UnitTests/Facturas/FacturaVentaAggregateTests.cs`: inmutabilidad, longitudes y totales físicos.
- `backend/tests/Facturacion.UnitTests/Facturas/EmitirFacturaVentaHandlerTests.cs`: copia de datos y peso, precedencia de condición del pedido/captura y PUE/PPD, conservación del total sin partidas duplicadas, datos de impresión de captura sin pedido, límites de medidas/referencia, alcance/versionado y mantenimiento del timbre. Se conservan las pruebas de la base.
- `backend/tests/Facturacion.UnitTests/Timbrado/CadenaOriginalTimbreTests.cs`: XML ficticio con/sin leyenda, certificado, entradas incompletas y rechazo de DTD.
- `backend/tests/Facturacion.UnitTests/Timbrado/CfdiEmisionBuilderTests.cs`: ACT, descripciones y ausencia de datos de impresión en el contrato enviado al proveedor fiscal. Esto no acredita un timbrado real del PAC.
- `frontend/.../components/DatosClienteFactura.test.tsx`: edición del borrador, sólo lectura por estado/permiso y límites.
- `frontend/.../components/emitir-factura/EmitirFacturaForm.smoke.test.tsx`: anticipos más comentarios del pedido y conservación de edición manual.

## Resultados de verificación

| Verificación | Resultado |
|---|---|
| `dotnet build Millet.sln -warnaserror --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` desde `backend` | **Salida 0**, 0 advertencias y 0 errores; solución completa (116.62 segundos). |
| Facturacion.UnitTests | **646 aprobadas, 0 fallos, 0 omitidas**, runner xUnit en proceso (9.595 segundos), salida 0. |
| `tsc -b tsconfig.app.json tsconfig.node.json` desde `frontend` | **Salida 0**, sin errores. |
| `eslint src` desde `frontend` | **Salida 0**, 0 errores, 1 advertencia fuera del cambio: `src/modules/administracion/components/EmpleadoInlineForm.tsx:708`, `react-hooks/incompatible-library`. |
| `vitest run src/features/facturacion --maxWorkers=2 --testTimeout=60000` | **354 aprobadas en 69 archivos**, salida 0 (128.64 segundos). |
| Pruebas focalizadas del bloque y formulario de emisión | 43 aprobadas en 2 archivos; incluye anticipos más comentarios y conservación de edición manual. |
| Pruebas focalizadas de detalle de pedido y factura | 20 aprobadas en 2 archivos. |
| `dotnet ef migrations has-pending-model-changes --no-build` | **Salida 0**, sin cambios pendientes del modelo. |
| `git diff --check` | Sin errores de espacios ni marcadores de conflicto. |

### Migración

Generada después de compilar Facturación con:

```sh
cd backend
dotnet ef migrations add FmtFacturaDatosImpresion --context FacturacionDbContext --project src/Facturacion/Millet.Facturacion.csproj --startup-project src/Api/Millet.Api.csproj --output-dir Infrastructure/Persistence/Migrations --no-build
```

Archivos, relativos a `backend/src/Facturacion/Infrastructure/Persistence/Migrations/`:

- `20261022223523_FmtFacturaDatosImpresion.cs`.
- `20261022223523_FmtFacturaDatosImpresion.Designer.cs`.
- `FacturacionDbContextModelSnapshot.cs`, generado por EF, sin edición manual.

Revisado el `Up`: agrega **12 columnas opcionales**, sólo en el esquema `facturacion` (6 en partidas de pedido/factura, 4 en cabecera de factura y 2 en comprobante). No borra ni reescribe datos. Conserva `Observaciones`, que ya existía; no agrega `Comentarios` a la factura. Los totales calculados no generan columnas. `Down` retira únicamente esas columnas nuevas.

### Límites del entorno y cómo se ejecutaron las unitarias

- VSTest normal (`dotnet test`) compiló módulo y suite, pero no pudo iniciar la ejecución: `System.Net.Sockets.SocketException (13): Permission denied` al abrir su socket local. **No se cuenta como una corrida aprobada de VSTest.**
- Se ejecutó la misma suite compilada con `Xunit.Runners.AssemblyRunner` de xUnit 2.8.2 ya instalado, dentro del mismo proceso y sin socket. El auxiliar de `/tmp/fmt3-inproc-runner` se compiló sin paquetes nuevos, usando las dependencias de la suite. Verifica un total mayor a cero y sale con error ante fallos; devolvió **646/646 aprobadas**. Se retiraron sus dos DLL auxiliares de la carpeta ignorada de salida de pruebas; no se agregó infraestructura al repositorio.
- Antes de la corrida definitiva hubo compilaciones canceladas por lentitud y una ejecución parcial de Vitest con fallos por tiempo de espera (límite original de 15 segundos). Se repitió la suite completa con dos procesos y 60 segundos por prueba; el resultado definitivo es el de la tabla.
- No se desactivaron analizadores en la compilación aprobada. Se usó `DOTNET_PROCESSOR_COUNT=2` y un `DOTNET_CLI_HOME` temporal para los comandos de .NET.

Evidencia local de esta continuación: `/tmp/fmt3-solution-final.log`, `/tmp/fmt3-inproc-result.log`, `/tmp/fmt3-current-tsc.log`, `/tmp/fmt3-current-eslint.log`, `/tmp/fmt3-all-vitest-final.log`, `/tmp/fmt3-migration-generated.log` y `/tmp/fmt3-modelcheck.log`. Los logs de `/tmp` son temporales; este resumen conserva los resultados.

## Fuera de alcance y pendientes externos

- No se modificó `QuestPdfFacturaGenerator.cs`; el rediseño e impresión de nuevos campos corresponde a FMT-4.
- No se modificó Integraciones.Aw, Activos Fijos ni la captura por total de anticipos.
- No se aplicó la migración, no se ejecutaron integraciones con Docker, no se timbró en sandbox del PAC y no se verificó aceptación del cliente: **Por confirmar**, a cargo de Claude/entorno correspondiente.
- La inspección visual de la aplicación conectada al backend queda **Por confirmar**.

## Siguiente acción para Claude

1. Marcar los tres archivos indicados en «Conflictos» con `git add`, desde un entorno que pueda escribir el índice compartido del worktree. El contenido está resuelto y probado; **no hacer commit como parte de esta corrida**.
2. Revisar el diff de FMT-3 contra la base del PR #191, incluyendo los tres archivos de la migración.
3. Aplicar la migración en PostgreSQL desechable, ejecutar las integraciones de Facturación y validar el anticipo ACT en el sandbox del PAC si tiene acceso.
4. Verificar visualmente captura y detalle conectados al backend. La impresión de los nuevos datos continúa en FMT-4.


## Correcciones del 22-oct

Aplicada únicamente la sección FMT-3 de `02_Correcciones_revision_22oct.md`, después de leer completos ese documento, `00_Reglas_comunes.md` y `01_Continuacion_22oct.md`. Se conservaron los cambios previos del worktree. No se ejecutó `git add`, commit, push ni cambio de rama.

### Cambios por archivo y decisiones

- `backend/src/Facturacion/Application/Facturas/EmitirFacturaVenta/EmitirFacturaVentaHandler.cs`: el emparejamiento de partidas conserva posición y producto, sin exigir cantidades iguales. Peso y área se prorratean por cantidad facturada/cantidad del pedido y se conserva la referencia. Continúa el respaldo por peso unitario cuando el pedido no trae peso. La selección de observaciones y referencia ya utilizaba `??`: se conserva la omisión sólo para `null`; una cadena vacía explícita no vuelve a tomar comentarios ni número del pedido.
- `backend/src/Facturacion/Domain/Facturas/FacturaVenta.cs`: `EditarDatosCliente` conserva observaciones vacías como `""`, después de validar longitud. Evita que la normalización las vuelva `null`. **No se modificó `FijarObservaciones`**, conforme a la exclusión de #191.
- `backend/src/Facturacion/Domain/Comprobantes/CadenaOriginalTimbre.cs`: elimina el BOM inicial antes de leer el XML, conservando la prohibición de DTD y resolución externa.
- `backend/src/Facturacion/Application/Timbrado/TimbradoEjecutor.cs`: registra una advertencia con el identificador del comprobante si un timbrado confirmado queda sin cadena original. Reutiliza la fábrica de logging del contexto EF, sin cambiar firmas ni sus consumidores. No incluye XML ni sellos en el log ni revierte el timbrado confirmado.
- `frontend/src/features/facturacion/components/emitir-factura/EmitirFacturaForm.tsx`: envía `""` al borrar las observaciones, en vez de convertirlo a `null`.
- `frontend/src/features/facturacion/components/emitir-factura/TabEncabezado.tsx`: observa por nombre los cinco campos de datos para el cliente. `CONTADO` se muestra como placeholder para PUE cuando no hay valor, sin rellenar visualmente el campo como si fuera un dato capturado.
- `frontend/src/features/facturacion/components/DatosClienteCampos.tsx`: recibe el placeholder de condiciones de pago del formulario de emisión.
- `frontend/src/features/facturacion/components/DatosClienteFactura.tsx`: la identidad del editor depende del comprobante, sin la versión. La actualización después del guardado conserva el mensaje «Datos guardados» y el siguiente envío usa la versión nueva.
- `frontend/src/features/facturacion/pages/DetalleFactura.tsx`: los totales de kilos y pies² muestran «—» cuando sus partidas no tienen la medida correspondiente; con medidas usan el formateador compartido `formatCantidad`, con separadores de miles. Un cero explícito en las medidas sigue siendo válido.

### Pruebas y resultados de esta revisión

- `backend/tests/Facturacion.UnitTests/Facturas/EmitirFacturaVentaHandlerTests.cs`: ajustada la copia de datos para probar omisión, edición y borrado intencional. Agregados pedido de 10 facturado en 4 (25 kg → 10 kg; 50 pies² → 20 pies²) y advertencia de timbrado sin cadena, conservando estado Timbrado.
- `backend/tests/Facturacion.UnitTests/Timbrado/CadenaOriginalTimbreTests.cs`: nuevo XML con BOM; verifica cadena, UUID y certificado del emisor.
- `frontend/src/features/facturacion/components/emitir-factura/EmitirFacturaForm.smoke.test.tsx`: borrado intencional de comentarios prellenados, payload vacío sin nota y `CONTADO` como ejemplo con valor vacío.
- `frontend/src/features/facturacion/components/DatosClienteFactura.test.tsx`: el mensaje de guardado y la edición sobreviven al incremento de versión; el siguiente guardado envía la versión actualizada.
- `frontend/src/features/facturacion/pages/DetalleFactura.smoke.test.tsx`: totales sin medidas y con medidas y formato numérico.

| Verificación | Resultado |
|---|---|
| Compilación de `Millet.Facturacion.UnitTests.csproj` y dependencias, `--no-restore -warnaserror -m:2 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false` | Aprobada. |
| Suite completa del proyecto Facturacion.UnitTests, xUnit en proceso | **651 aprobadas, 0 fallos, 0 omitidas**, salida 0. |
| `npx tsc -b tsconfig.app.json tsconfig.node.json` | Salida 0. |
| `npx eslint src/features/facturacion` | Salida 0, sin diagnósticos. |
| Vitest de DatosClienteFactura, EmitirFacturaForm, DetalleFactura smoke y DetalleFactura cobro, `--maxWorkers=2 --testTimeout=60000` | **58 aprobadas en 4 archivos**, salida 0. |
| `git diff --check` y conflictos de integración | Sin errores de espacios; no hay archivos sin resolver. |
| Comparación de `FijarObservaciones` con el índice existente | Sin cambios. |

El intento inicial con `-m:2` encontró restricciones de tuberías de MSBuild. Se conservó `-m:2` con `BuildInParallel=false`, evitando nodos externos. VSTest compiló la suite actualizada, pero su ejecución fue anulada porque el sandbox impide abrir su socket local (`SocketException (13): Permission denied`); no se reporta como VSTest aprobado. Se ejecutó la misma DLL de pruebas con el auxiliar xUnit ya existente en `/tmp/fmt3-inproc-runner`, sin paquetes nuevos ni infraestructura agregada al repositorio. Las DLL auxiliares copiadas a la salida ignorada se retiraron después. .NET usó `DOTNET_PROCESSOR_COUNT=2` y `DOTNET_CLI_HOME=/tmp/fmt3-dotnet-home`.

Evidencia temporal: `/tmp/fmt3-correcciones-build-final.log`, `/tmp/fmt3-correcciones-backend-final.log` (limitación VSTest), `/tmp/fmt3-correcciones-inproc.log`, `/tmp/fmt3-correcciones-vitest-final.log`, `/tmp/fmt3-correcciones-tsc-final.log` y `/tmp/fmt3-correcciones-eslint-final.log`.

### Pendientes y siguiente acción

Estas correcciones no modifican el modelo EF ni requieren regenerar la migración existente. No se ejecutaron la solución completa ni las suites de otros módulos para respetar el alcance y la carga indicados en la revisión. Integración con PostgreSQL/Docker, PAC real y revisión visual de la aplicación conectada: **Por confirmar**. No se tocó FMT-1a, FMT-1b ni el generador PDF de FMT-4. Este resumen es un artefacto de revisión local, no parte del producto.

Siguiente acción recomendada: Claude revisa el diff de FMT-3, ejecuta la suite completa y la integración en su entorno, y realiza la comprobación visual antes de integrar. El índice y los commits quedan a su cargo.
