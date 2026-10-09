# Entrega P4 · Saldos CxP / Tesorería y REPP de proveedor

Estado: implementación y verificación local completadas; **integración PostgreSQL y ensayo en vivo: Por confirmar**.
Base verificada: `6718ffe` (HEAD y `origin/main` al iniciar). Rama: `fix/P4-saldos-cxp-tesoreria`.
Trabajo limitado al worktree actual, sin commits ni push, sin `.env*` ni servidores en 5080/5173.

## Qué cambió y por qué

- Nota de cargo: factura origen obligatoria al aplicar, mismo proveedor/moneda y saldo suficiente. Nuevo `CargosAplicadosTotal`, saldo/elegible recalculados y mismo aviso de P3 a Tesorería. El frontend distingue «Aplicada sin formalizar» y «Formalizada». Una NC 03 coincidente formaliza al capturarse, al vincularse o mediante el worker; ante coincidencia ambigua se exige elección manual. También hay comando y acción para ligar manualmente la NC.
- Aplicaciones posteriores: «Aplicar anticipo» y «Aplicar NC» en el detalle de factura, documentos abiertos, moneda y saldo antes/después; se reutilizan los comandos y eventos de P3. La NC fiscal que reconoce un cargo no descuenta el mismo importe por segunda vez.
- NC manual: UUID relacionado debe coincidir con la factura; excepción explícita con motivo persistido. Moneda y proveedor también deben coincidir.
- D9: configuración por empresa/proveedor en tabla propia, FANT por omisión. UUID, serie, folio y fecha proceden del CFDI ligado, que se marca consumido. UUID controlado entre facturas, NC y anticipos bajo candado transaccional.
- D10: NC 07 vinculada al UUID del anticipo y amortización interna. La NC 07 puede reconocer la aplicación interna ya realizada sin consumir otra vez el anticipo. Se serializa esa operación para evitar reconocimientos simultáneos del mismo saldo.
- Cancelaciones: comandos y endpoints de anticipo, NC y cargo con motivo y versión. Se mantienen los candados del dominio sobre documentos aplicados; cancelar no borra ni revierte una aplicación de forma implícita.
- TES-03: retiro de autorización en revisión, cancelación o solicitud de cancelación mediante evento por Outbox antes de SaveChanges. Tesorería conserva el bloqueo incluso si el evento llega antes de la autorización y rechaza el pago con 422. Se conserva también la consulta directa de elegibilidad de P3 para la ventana de entrega del evento.
- TES-08: revisión diaria de facturas PPD con pago pendiente de complementar al superar cinco días hábiles, incluso Pagadas. Calendario de P1, excluyendo día del pago, fines de semana y festivos. FALTA_REPP solo se libera con cobertura completa de los pagos; conserva el estado Pagada cuando el saldo está liquidado.
- REPP: XML obligatorio, RFC emisor del proveedor, UUID/tipo/fecha del CFDI, UUID de factura, moneda, importes, saldos y parcialidad. Uno o varios pagos previos de la misma factura con importe por pago. La bandeja muestra remanente fiscal por pago; rechaza pagos inexistentes/revertidos y cobertura excesiva. Los registros anteriores sin desglose pueden completarse una sola vez validando su XML.
- Pagos existentes: puerto público de lectura de Tesorería para reconstruir la proyección local de pagos y coberturas durante la revisión diaria. No se escriben tablas de otro módulo.

## Verificación previa y decisiones de compatibilidad

P3 ya descontaba cargos y avisaba a Tesorería; almacenaba ese importe dentro de NC. Se conservó el recorrido y se separó el acumulado. P3 también avisaba al aplicar anticipo y NC: se reutilizó. P8 ya centralizaba el candado de periodo y el historial: se mantuvo.

P3 permite recapturar una factura rechazada por tolerancia después de liberar su CFDI. Se conserva ese reintento con el mismo CFDI; no permite reutilizar ese UUID como NC o anticipo. Las pruebas de P3 siguen en verde.

La suscripción de Tesorería ya existía: se amplió únicamente su filtro en `tools/servicebus-emulator/Config.json` y `infra/modules/servicebus.bicep`. No hubo otros cambios en infra.

**No se sembró ningún ParametroGlobal ni se usaron IDs nuevos del catálogo compartido.** La configuración de serie tiene tabla propia; FALTA_REPP utiliza el motivo existente. No se reescribieron funcionalidades de P5/P9.

## Migraciones

- `20261009202105_P4SaldosCxp`: columnas de cargo/relación de anticipo/excepción, configuración por proveedor y proyección de pagos. Separa los cargos históricos usando el ledger de P3, preservando el saldo. Si el historial excede el acumulado original, detiene la migración para revisión, sin ajustar cifras silenciosamente.
- `20261009202242_P4ReppPagosTesoreria`: bloqueo de pasivo y coberturas REPP por aplicación de pago.

Ambas migraciones y sus snapshots están generados y compilados. **No se aplicaron a una base PostgreSQL en este sandbox.** Los REPP anteriores sin XML validado/desglose no se consideran automáticamente cobertura fiscal; deben completarse con su XML y pagos.

## Pruebas y resultados exactos

| Verificación | Resultado |
| --- | --- |
| `dotnet build Millet.sln --no-restore -m:1 -nr:false` | 0 errores, 0 advertencias |
| CxP unitarias | 439 pasaron, 0 fallaron, 0 omitidas |
| Tesorería unitarias | 119 pasaron, 0 fallaron, 0 omitidas |
| SharedKernel unitarias | 258 pasaron, 0 fallaron |
| Compartido unitarias | 89 pasaron, 0 fallaron |
| `npx tsc --noEmit -p tsconfig.json` | salida 0 |
| `npx tsc --noEmit -p tsconfig.app.json` | salida 0; comprobación adicional del código de aplicación |
| `npm run -s typecheck:test` | salida 0 |
| `npm run -s lint` | 0 errores; 10 avisos previos en Administración |
| Vitest completo `--maxWorkers=2` | 355 archivos, 2,066 pruebas pasaron |
| Vitest final CxP/Tesorería `--maxWorkers=2` | 29 archivos, 90 pruebas pasaron |
| `git diff --check` | sin errores |

Restauración .NET desde caché local, sin auditoría NuGet remota. VSTest fue anulado por `SocketException (13): Permission denied` al abrir el socket de comunicación local. Las unitarias se ejecutaron con el motor real xUnit en proceso (XunitFrontController, sin VSTest), incluyendo teorías y fixtures. Los logs están en [evidencia-p4](evidencia-p4/resultados.json).

Pruebas nuevas: `SaldosP4Tests` cubre $50,000 − $10,000, formalización tardía, moneda, UUID entre tipos, cancelaciones, quinto/sexto día hábil con festivo y fin de semana, liberación parcial/completa en autorizada/pagada, D9 y NC07 sin duplicar aplicación interna. `P4XmlYRetiroTests` cubre XML correcto y mal emitido, RFC, factura, importe, saldo, moneda, parcialidad, timbre, fecha, DTD y entrega desordenada del retiro. `ReppRecibidoTests` agrega cobertura del registro anterior sin desglose. Frontend: prueba del cálculo puro y de aplicar anticipo/NC desde la pantalla con versiones/idempotencia y rechazo de moneda.

`P4EndpointsTests` deja tres escenarios de integración HTTP/PostgreSQL: cargos/formalización/aplicaciones/configuración/cancelación; retiro de autorización y 422 en Tesorería antes y después del evento; REPP mal emitido, vencimiento hábil y cobertura parcial/completa de una factura pagada. Usan datos FIX, puertos simulados y limpieza por proveedor; no alteran catálogos compartidos. El proyecto de integración compiló; estos escenarios **no se ejecutaron** aquí.

## Pendiente y siguiente acción

Claude debe ejecutar desde este worktree `tools/validate-integration-isolated.sh` completo y documentar rojo/verde, incluida la aplicación de migraciones y los tres escenarios P4. Después, realizar el recorrido en vivo de Millet con XML de proveedor representativo y la suscripción Service Bus efectiva. No hay evidencia de despliegue, ejecución real del worker diario ni aceptación de Millet en este trabajo.

Antes de integrar la rama, comprobar si P5/P9 llegaron a main y resolver únicamente los puntos de contacto, repitiendo los controles afectados. No se hizo merge/rebase mientras había cambios locales.

Mensaje de commit propuesto (no ejecutado):

```text
fix(cxp-tesoreria): ajustar saldos, retirar pasivos y validar REPP del proveedor
```

## Archivos cambiados

- `backend/src/Api/Endpoints/CuentasPorPagar/AnticiposEndpoints.cs`
- `backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs`
- `backend/src/Api/Endpoints/CuentasPorPagar/NotasCreditoEndpoints.cs`
- `backend/src/Api/Infrastructure/Adapters/PagosProveedorReadPortAdapter.cs`
- `backend/src/Api/Program.cs`
- `backend/src/Compartido/Infrastructure/Calendario/CalendarioHabilService.cs`
- `backend/src/CuentasPorPagar/Application/AnticipoProveedor/CapturarAnticipo/CapturarAnticipoCommand.cs`
- `backend/src/CuentasPorPagar/Application/AnticipoProveedor/ConfiguracionAnticipoCommands.cs`
- `backend/src/CuentasPorPagar/Application/EventListeners/ContratosEspejo.cs`
- `backend/src/CuentasPorPagar/Application/EventListeners/Tesoreria/TesoreriaEventListenerCommands.cs`
- `backend/src/CuentasPorPagar/Application/FacturaProveedor/Elegibilidad/ElegibilidadFacturaService.cs`
- `backend/src/CuentasPorPagar/Application/FacturaProveedor/Queries/FacturasEnRevisionPorAreaQuery.cs`
- `backend/src/CuentasPorPagar/Application/FacturaProveedor/Queries/GetFacturaPorIdQuery.cs`
- `backend/src/CuentasPorPagar/Application/FacturaProveedor/Queries/ListarFacturasQuery.cs`
- `backend/src/CuentasPorPagar/Application/FacturaProveedor/RevisionRepp/RevisarFaltaReppCommand.cs`
- `backend/src/CuentasPorPagar/Application/Integration/PasivoRetiradoDePagoIntegrationEvent.cs`
- `backend/src/CuentasPorPagar/Application/NotaCargo/FormalizacionNotaCargoService.cs`
- `backend/src/CuentasPorPagar/Application/NotaCargo/NotaCargoCommands.cs`
- `backend/src/CuentasPorPagar/Application/NotaCargo/P4DocumentosCommands.cs`
- `backend/src/CuentasPorPagar/Application/NotaCreditoProveedor/CapturarNotaCredito/CapturarNotaCreditoCommand.cs`
- `backend/src/CuentasPorPagar/Application/NotaCreditoProveedor/Queries/ListarNotasCreditoQuery.cs`
- `backend/src/CuentasPorPagar/Application/NotaCreditoProveedor/VincularFactura/VincularFacturaNotaCreditoCommand.cs`
- `backend/src/CuentasPorPagar/Application/Reportes/Comun/SaldosHistoricos.cs`
- `backend/src/CuentasPorPagar/Domain/AnticipoProveedor/AnticipoProveedor.cs`
- `backend/src/CuentasPorPagar/Domain/AnticipoProveedor/ConfiguracionAnticipoProveedor.cs`
- `backend/src/CuentasPorPagar/Domain/FacturaProveedor/FacturaProveedor.cs`
- `backend/src/CuentasPorPagar/Domain/FacturaProveedor/PagoProveedorLocal.cs`
- `backend/src/CuentasPorPagar/Domain/NotaCargo/NotaCargo.cs`
- `backend/src/CuentasPorPagar/Domain/NotaCreditoProveedor/NotaCreditoProveedor.cs`
- `backend/src/CuentasPorPagar/Domain/Ports/Tesoreria/IPagosProveedorReadPort.cs`
- `backend/src/CuentasPorPagar/Infrastructure/DependencyInjection.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Persistence/Configurations/AnticipoProveedorConfiguration.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Persistence/Configurations/FacturaProveedorConfiguration.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Persistence/CuentasPorPagarDbContext.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Persistence/Migrations/20261009202105_P4SaldosCxp.Designer.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Persistence/Migrations/20261009202105_P4SaldosCxp.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Persistence/Migrations/CuentasPorPagarDbContextModelSnapshot.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Workers/FaltaReppWorker.cs`
- `backend/src/CuentasPorPagar/Infrastructure/Workers/NotaCreditoEnEsperaMatchWorker.cs`
- `backend/src/SharedKernel/Application/Calendario/CalendarioHabil.cs`
- `backend/src/SharedKernel/Application/Calendario/ICalendarioHabil.cs`
- `backend/src/Tesoreria/Application/EventListeners/ProyectarPasivoAutorizadoCommand.cs`
- `backend/src/Tesoreria/Application/EventListeners/RetirarPasivoDePagoCommand.cs`
- `backend/src/Tesoreria/Application/Integration/TesoreriaIntegrationEvents.cs`
- `backend/src/Tesoreria/Application/Pagos/PagosCommands.cs`
- `backend/src/Tesoreria/Application/Pasivos/BandejaPasivosPendientesQuery.cs`
- `backend/src/Tesoreria/Application/PublicPorts/ConsultarPagosProveedorQuery.cs`
- `backend/src/Tesoreria/Application/Repp/RegistrarReppRecibidoCommand.cs`
- `backend/src/Tesoreria/Application/Repp/ReppPendientesQuery.cs`
- `backend/src/Tesoreria/Application/Repp/ReppXmlValidator.cs`
- `backend/src/Tesoreria/Domain/Pasivos/PasivoPendientePago.cs`
- `backend/src/Tesoreria/Domain/Ports/DatosMaestros/IProveedorBancoReadPort.cs`
- `backend/src/Tesoreria/Domain/Repp/ReppPagoProveedor.cs`
- `backend/src/Tesoreria/Domain/Repp/ReppProveedorRecibido.cs`
- `backend/src/Tesoreria/Infrastructure/Adapters/ProveedorBancoReadPortAdapter.cs`
- `backend/src/Tesoreria/Infrastructure/Persistence/Migrations/20261009202242_P4ReppPagosTesoreria.Designer.cs`
- `backend/src/Tesoreria/Infrastructure/Persistence/Migrations/20261009202242_P4ReppPagosTesoreria.cs`
- `backend/src/Tesoreria/Infrastructure/Persistence/Migrations/TesoreriaDbContextModelSnapshot.cs`
- `backend/src/Tesoreria/Infrastructure/Persistence/TesoreriaDbContext.cs`
- `backend/src/Tesoreria/Infrastructure/Workers/CuentasPorPagarEventListenerWorker.cs`
- `backend/tests/Api.IntegrationTests/CuentasPorPagar/P4EndpointsTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/AnticipoProveedor/AnticipoProveedorAggregateTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/P4/SaldosP4Tests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/P8/AplicacionesP8Tests.cs`
- `backend/tests/Tesoreria.UnitTests/Repp/P4XmlYRetiroTests.cs`
- `backend/tests/Tesoreria.UnitTests/Repp/ReppRecibidoTests.cs`
- `frontend/src/features/cxp/api/types.ts`
- `frontend/src/features/cxp/api/useNotasYAnticipos.ts`
- `frontend/src/features/cxp/components/AplicarDocumentoFacturaSheet.test.tsx`
- `frontend/src/features/cxp/components/AplicarDocumentoFacturaSheet.tsx`
- `frontend/src/features/cxp/components/DocumentoP4Acciones.tsx`
- `frontend/src/features/cxp/components/NuevoAnticipoSheet.tsx`
- `frontend/src/features/cxp/components/SerieAnticipoProveedor.tsx`
- `frontend/src/features/cxp/lib/aplicaciones-p4.test.ts`
- `frontend/src/features/cxp/lib/aplicaciones-p4.ts`
- `frontend/src/features/cxp/pages/AnticiposPage.tsx`
- `frontend/src/features/cxp/pages/FacturaDetallePage.tsx`
- `frontend/src/features/cxp/pages/NotaCargoDetallePage.tsx`
- `frontend/src/features/cxp/pages/NotaCreditoDetallePage.tsx`
- `frontend/src/features/cxp/schemas/notas-y-anticipos.ts`
- `frontend/src/features/tesoreria/api/types.ts`
- `frontend/src/features/tesoreria/api/useTesoreria.ts`
- `frontend/src/features/tesoreria/components/RegistrarReppSheet.tsx`
- `frontend/src/features/tesoreria/pages/BandejaReppPendientes.tsx`
- `infra/modules/servicebus.bicep`
- `tools/servicebus-emulator/Config.json`

Logs de verificación y metadatos: `docs/entregas/evidencia-p4/`.
