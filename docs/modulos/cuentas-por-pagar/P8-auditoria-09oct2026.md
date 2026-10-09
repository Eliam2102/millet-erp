# P8 · Cierre de periodo, reportes y retenciones de CxP

Estado: implementación local verificada; cierre de integración **Por confirmar**.
Fecha: 9 de octubre de 2026. Empresa: Vidrios Millet.
Rama: `fix/P8-cierre-reportes-cxp`. HEAD/base local: `d6116e2`, descendiente de `fix/P3-factura-exacta-G1.4`.
No se hicieron commits, push, publicación, aplicación de migraciones ni cambios en Compras, infraestructura o `.env*`.

## Verificación de la ficha contra esta base

- CxP no tenía puerto de periodo; la validación de Contabilidad existe por `IPeriodoContableConsultaPort`, ADR-0059. Se reutilizó ese contrato mediante un puerto/adaptador de CxP.
- P3 ya conserva `RetencionesDetalle`, concilia factura/NC por línea y calcula elegibilidad. Se conservó su lógica. El fixture de P3 recibió únicamente el puerto de periodo abierto necesario para el nuevo constructor del contexto.
- RQ (`Compras/Domain/Requisicion.cs`), OC (`Compras/Domain/Oc/OrdenCompra.cs`) y su DTO de lectura no tienen obra en esta base. Se aplicó el fallback expresamente autorizado de D11: obra capturable/editable en el pasivo, independiente de sucursal. Su herencia desde RQ/OC se conectará en P7.
- El maestro `DatosMaestros/Domain/Proveedor.cs` y `ProveedorDto` no tienen subcategoría. Se omitió la columna; no se inventó un catálogo ni se cambió el maestro.
- Las aplicaciones anteriores solo conservaban acumulados, sin historial fechado suficiente para todos los cortes. No se fabricaron fechas para datos anteriores a P8.

## Comportamiento implementado y decisiones

1. **Periodos.** El contexto valida todas las persistencias financieras de factura, anticipo, NC y cargo, incluyendo capturas, autorización, aplicaciones, cancelación y cambio de fecha. Rechaza periodo cerrado, sin abrir o inexistente con `422 CXP_PERIODO_CERRADO` y mensaje en español. Cambiar la fecha exige que estén abiertos tanto el periodo anterior como el nuevo. Las aplicaciones usan su fecha de operación; pagos y reversos conservan la fecha informada por Tesorería. Se usa el mismo candado transaccional PostgreSQL de ADR-0059 hasta commit para evitar carrera con cierre. La apertura/reapertura permanece en Contabilidad.
2. **Historial.** `movimientos_pasivo` conserva aplicaciones fechadas de pago, reverso, anticipo, NC y cargo. El reverso es un movimiento negativo. Los reportes reconstruyen el saldo al corte con documentos hasta el día solicitado y movimientos hasta ese corte, aun si la factura se pagó o canceló después. Se conserva la convención de fechas UTC de los campos del módulo; el final del corte es exclusivo al inicio del día siguiente.
3. **Reportes.** Antigüedad, cartera, anticipos, pasivos por obra y nuevo auxiliar. Nombre/RFC, agrupación proveedor/moneda, tramo por vencer, totales `por_moneda`, anticipo con saldo positivo al corte. Cartera conserva estado de revisión histórico; no agrega una subcategoría inexistente. Los reportes de facturas limitan sucursales asignadas salvo permiso corporativo nuevo. Obra es filtro propio. UI/exportadores reciben resúmenes separados por moneda; el adaptador evita aplicar el formato fijo MXN del shell a importes USD.
4. **Datos anteriores a P8.** Si los acumulados de una factura/anticipo no coinciden con movimientos fechados reconstruibles, el reporte responde `CXP_HISTORICO_POR_CONFIRMAR`. No presenta el saldo actual como histórico. La migración crea el historial hacia adelante; no hace un backfill sin evidencia. Es necesario reconstruir/validar esas aplicaciones antes de conciliar saldos anteriores.
5. **Retenciones.** Catálogo compartido administrable con impuestos SAT ISR/IVA/IEPS, tasa, concepto, fuente, estado y motivo obligatorio. Permisos de lectura/administración, concurrencia por versión, idempotencia y bitácora transaccional `core.audit_log` mediante `IAuditable`. Seed de seis reglas: ISR honorarios 10 %, ISR arrendamiento 10 %, IVA dos terceras partes de 16 % en ambos conceptos, fletes 4 % y RESICO 1.25 %. Todas llevan **“Supuesto SAT, valida Fiscal (D03)”**. Para manual propone importe/detalle; para CFDI conserva los importes del XML y genera alerta sin bloqueo cuando difieren del concepto. La alerta se guarda y se muestra en detalle.
6. **Alcance SAT.** El seed contiene las reglas expresamente solicitadas; se pueden administrar más conceptos/impuestos/tasas con fuente y motivo. No se afirma una importación exhaustiva ni sincronización automática de `c_TasaOCuota`: esa tabla contiene tasas/rangos por impuesto/factor y no asigna por sí sola todos los conceptos de negocio. El mapeo adicional por concepto/régimen y su aprobación quedan a Fiscal (D03); no se inventaron conceptos de Millet.
7. **Cargo y elegible.** Aplicar cargo ahora descuenta el pasivo y guarda su movimiento. Una NC que formaliza ese cargo reconoce solo el monto que consta en el historial, evitando doble descuento. Cargo, anticipo y NC actualizan el elegible autorizado utilizando el mapper existente y publican antes de `SaveChanges` (ADR-0009), sin cambiar la conciliación ni el cálculo de P3.

## Fuentes de las propuestas fiscales

Fuentes oficiales consultadas; las propuestas siguen sujetas a D03:

- [Ejemplo SAT de honorarios](https://www.sat.gob.mx/minisitio/Factura/documentos/honorarios_servicios_contables.pdf).
- [Ejemplo SAT de arrendamiento](https://www.sat.gob.mx/minisitio/Factura/documentos/arrendamiento_local_comercial.pdf).
- [Ley del ISR en el SAT](https://wwwmat.sat.gob.mx/ordenamiento/18355/ley-del-impuesto-sobre-la-renta).
- [Artículo 113-J, RESICO](https://wwwmat.sat.gob.mx/articulo/59511/articulo-113-j).
- [Reglamento de IVA, artículo 3](https://www.sat.gob.mx/cs/Satellite?blobcol=urldata&blobkey=id&blobtable=MungoBlobs&blobwhere=1461175803212&ssbinary=true).
- [Guía SAT sobre tasas fijas/variables de CFDI](https://www.sat.gob.mx/cs/Satellite?blobcol=urldata&blobkey=id&blobtable=MungoBlobs&blobwhere=1461173536663&ssbinary=true).

## Archivos y rutas principales

- Dominio/contexto: `Domain/FacturaProveedor/{FacturaProveedor,MovimientoPasivo}.cs`, `Domain/Ports/Contabilidad/IPeriodoContablePort.cs`, `Infrastructure/Adapters/PeriodoContableAdapter.cs`, `Infrastructure/Persistence/CuentasPorPagarDbContext.cs`.
- CQRS: `Application/Periodos/`, `Application/Reportes/{Comun,AntiguedadSaldos,CarteraPorCategoriaRevision,AntiguedadAnticipos,PasivosObras,AuxiliarProveedores}/`, `Application/Catalogos/Retenciones/`.
- Captura/edición/aplicaciones y listeners de Tesorería; endpoints de facturas, reportes y catálogo; DI y permisos canónicos de Identidad.
- Frontend: `features/cxp/api/{types,useReportes,useRetenciones}`, `lib/{reportes-adapter,retenciones-p8}`, captura y edición de datos del pasivo, detalle, reportes de obra/auxiliar, catálogo, rutas y navegación.
- Migraciones: `20261009165906_P8CierreReportesObraRetenciones` (CxP) y `20261009165916_P8PermisosRetencionesReportesCxp` (Identidad), con designers/snapshots. No se aplicaron a una base de datos.
- Inventario completo: [archivos-P8.txt](evidencia-P8/archivos-P8.txt).

## Pruebas y resultados exactos

| Verificación | Resultado | Evidencia |
| --- | --- | --- |
| Solución backend, 35 proyectos | **0 errores, 0 advertencias**, exit 0 | [build.txt](evidencia-P8/build.txt) |
| Unitarias de CxP, ejecución directa de métodos xUnit | **412 pasan, 0 fallan, 0 omitidas**; incluye **35 casos nuevos P8** y P3 | [resultado](evidencia-P8/unitarias-ejecucion-directa.txt), [ejecutor](evidencia-P8/ejecutor-directo.cs.txt) |
| `dotnet test` estándar de CxP | **Anulado**, exit 1: el sandbox deniega bind del socket de VSTest (`SocketException 13`) | [registro](evidencia-P8/unitarias-vstest-bloqueado.txt) |
| `npx tsc --noEmit -p tsconfig.json` | Exit 0, sin errores | [tipos](evidencia-P8/frontend-tipos.txt) |
| `npx tsc --noEmit -p tsconfig.app.json` | Exit 0; comprobación adicional del proyecto referenciado | [tipos](evidencia-P8/frontend-tipos.txt) |
| `npm run -s typecheck:test` | Exit 0, sin errores | [tipos](evidencia-P8/frontend-tipos.txt) |
| `npm run -s lint` | **0 errores, 10 advertencias previas** en Administración | [lint](evidencia-P8/frontend-lint.txt) |
| `npx vitest run` completo | **344 archivos y 2,005 pruebas aprobadas**, exit 0 | [vitest](evidencia-P8/frontend-vitest.txt) |
| EF `has-pending-model-changes`, CxP e Identidad | Sin diferencias modelo/migración, exit 0 | [CxP](evidencia-P8/modelo-cxp.txt), [Identidad](evidencia-P8/modelo-identidad.txt) |
| `git diff --check` | Sin errores | Comprobado al finalizar |
| PostgreSQL y HTTP real | **Por confirmar**; las 3 pruebas P8 de integración compilan | `backend/tests/Api.IntegrationTests/CuentasPorPagar/P8EndpointsTests.cs` |

Comando de build utilizado (restore previo desde cache local por restricción de red):

```sh
cd backend
DOTNET_CLI_HOME=/tmp/p8-dotnet MSBuildEnableWorkloadResolver=false dotnet build Millet.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
```

La ejecución directa es una comprobación suplementaria, no reemplaza el gate estándar: invoca cada Fact/Theory/InlineData del assembly compilado de CxP en proceso, esperando tareas y disposing fixtures. No usa sockets ni cambia las pruebas. Las migraciones se generaron/contrastaron con EF 10.0.12 mediante factories temporales de diseño, sin conexión a base de datos.

### Cobertura P8

- Periodo abierto/cerrado por captura, autorización, aplicación de anticipo/NC/cargo, cancelación y cambio de fecha; cambio desde periodo cerrado a abierto rechazado; captura de documentos auxiliares.
- Handlers de antigüedad/cartera/auxiliar a corte pasado: factura futura y aplicaciones posteriores excluidas; pagos/reversos/cancelaciones posteriores; MXN/USD y nombre/RFC; por vencer; anticipo completamente amortizado; filtro obra; ámbito de sucursales; rechazo explícito de historial incompleto.
- Handler de cargo y anticipo: actualización del elegible antes de guardar; NC formalizada sin doble descuento, incluyendo cargo antiguo sin movimiento.
- Catálogo: propuesta, desglose distinto aunque el total cuadre, alerta persistida sin bloqueo, control de versión.
- UI: administración con motivo/versión/idempotencia, lectura sin edición, auxiliar por corte con RFC/totales por moneda, filtro obra independiente, adopción manual y alerta de CFDI visible en detalle; cálculo puro y formato monetario.
- Integración escrita: periodos reales de Contabilidad y HTTP 422 sin persistencia parcial/outbox; historial y reportes HTTP sobre PostgreSQL; edición del catálogo con bitácora. Usa datos FIX-P8 y limpia sus registros nuevos; no cambia maestros compartidos ni cierra el calendario operativo.

## Pendientes y traspaso

1. **Claude:** ejecutar `tools/validate-integration-isolated.sh` completo con PostgreSQL desechable y conservar rojo/verde. Ejecutar también `dotnet test` estándar de CxP en un entorno que permita el socket de VSTest. Corregir cualquier falla antes de considerar listo para fusión.
2. **Millet:** recorrido en vivo de periodo cerrado/abierto, corte pasado, monedas, obra y alerta; verificación visual en navegador y exportaciones. Aquí se verificó render/interacción DOM mediante Testing Library, no una sesión real en navegador ni un PDF/Excel exportado por el usuario.
3. **P7:** completar herencia obra RQ → OC → pasivo por puerto; conectar obra interna ya disponible. Compras permanece intacto.
4. **Fiscal (D03):** validar seed, bases, regímenes/excepciones, conceptos adicionales y mapeo de tasas del SAT antes del uso definitivo. El catálogo permite ajustes auditados.
5. **Histórico anterior:** reconstruir aplicaciones a partir de evidencia de origen cuando exista. Saldos sin historia suficiente permanecen Por confirmar; no migrarlos con fechas supuestas.
6. **Motor de pólizas:** ejecutar conciliación auxiliar vs cuenta de proveedores cuando exista el motor. El auxiliar ya entrega saldo por proveedor/moneda a una fecha.
7. Actualización de bóveda preparada en [P8-borrador-actualizacion-boveda.md](P8-borrador-actualizacion-boveda.md), sin escritura fuera del worktree ni publicación.

Commit propuesto (no creado):

`feat(cxp): agregar cierre contable, reportes históricos y retenciones P8`

## Continuación · corrección de la adenda PostgreSQL del 9-oct

La adenda de Eliam/Claude reporta **883/884 pruebas API** y un HTTP 200 inesperado en el bucle de operaciones cerradas. Ese resultado es evidencia recibida; esta sesión no ejecutó PostgreSQL.

Se identificó el **PATCH `/facturas/{id}`**: el request reenvía la misma `FechaContabilizacion` y modifica el folio. EF no marca la fecha como modificada; `FechasContables()` omitía esa edición y `SaveChangesAsync` no consultaba el periodo. El handler ahora marca explícitamente la fecha contable para persistencia incluso cuando es idéntica. Así reutiliza la validación de fecha original y destino **bajo el advisory lock transaccional existente**, sin duplicar comprobaciones fuera de la transacción ni modificar P3.

- Producción: `Application/FacturaProveedor/EditarCabecera/EditarCabeceraFacturaCommand.cs`.
- Regresión: `PeriodosP8Tests.Editar_cabecera_con_la_misma_fecha_verifica_periodo`, cuatro casos (abierto/cerrado; folio cambiado/idéntico). Comprueba rechazo con código y ausencia de cambios persistidos en cerrado; acepta abierto.
- Integración: el bucle de `P8EndpointsTests` incluye ahora método, ruta, estado HTTP y respuesta en el mensaje de aserción tanto para el 422 como para el código de negocio.

| Verificación repetida en esta sesión | Resultado | Evidencia nueva |
| --- | --- | --- |
| Regresión antes de corregir, ejecución directa | **414 pasan, 2 fallan, 0 omitidas**; fallan ambos casos cerrados | [Rojo](evidencia-P8/continuacion-rojo-directo.txt) |
| Solución backend, con `--no-restore -m:1 -nr:false -p:UseSharedCompilation=false` y cache local | **0 errores, 0 advertencias**, exit 0; integración compila | [Build](evidencia-P8/continuacion-build.txt) |
| Unitarias CxP, ejecución directa después de corregir | **416 pasan, 0 fallan, 0 omitidas**; **39 casos P8** | [Verde](evidencia-P8/continuacion-verde-directo.txt) |
| `dotnet test` estándar CxP | **Anulado**, exit 1, `SocketException (13): Permission denied` al abrir el socket de VSTest | [VSTest](evidencia-P8/continuacion-unitarias-vstest.txt) |
| `npx tsc --noEmit -p tsconfig.json`, proyecto app y `npm run -s typecheck:test` | Los tres sin errores, exit 0 | [Tipos](evidencia-P8/continuacion-tipos.txt) |
| `npm run -s lint` | **0 errores, 10 advertencias** en Administración, exit 0 | [Lint](evidencia-P8/continuacion-lint.txt) |
| `npx vitest run` | **344 archivos y 2,005 pruebas pasan**, exit 0 | [Vitest](evidencia-P8/continuacion-vitest.txt) |
| `git diff --check`; límites de edición | Sin errores; sin cambios en Compras, `infra/` o `.env*`; sin commits/push | Inventario y diff del worktree |
| Corrección HTTP con PostgreSQL | **Por confirmar**; Claude debe repetir prueba P8 y suite aislada completa | `tools/validate-integration-isolated.sh` |

Se conserva la evidencia de la corrida anterior en la tabla inicial; los resultados de esta sección son el corte actual. No cambió el modelo ni se generaron nuevas migraciones. Permanecen los pendientes de P7, Fiscal, histórico anterior a P8, motor de pólizas y aceptación visual/en vivo descritos arriba. El catálogo inicial no es una importación exhaustiva de la tabla SAT.
