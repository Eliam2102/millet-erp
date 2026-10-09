# Entrega local P5 — Tesorería y Cobranza

Fecha: 9 de octubre de 2026. Rama: `fix/P5-tesoreria-cobranza`.
Funciones: F1-TES-01, F1-TES-03, F1-TES-07, F1-TES-11 y F1-CXC-02.

Estado: implementación local y pruebas unitarias verificadas. Integración PostgreSQL, transporte Service Bus, revisión visual y aceptación de Millet **Por confirmar**. Sin commit, push, despliegue ni cambio de tareas externas.

## Resultado por requisito

| Requisito | Implementación |
| --- | --- |
| CxC propone, Tesorería confirma | `PropuestoPor` viaja en propuesta, evento y depósito. Confirmar como proponente produce `DEP_MISMO_USUARIO`, sin excepción para super-admin. Se retiraron comandos, endpoints, hooks y botones de resolución directa de CxC. |
| Confirmación y rechazo | Consumidor de eventos de Tesorería en CxC, con resolución idempotente. El rechazo libera reservas; la confirmación las conserva hasta el REP. El REP actualiza cartera y libera la reserva sin aplicar dos veces el cobro. |
| Reserva de saldo | Se descuenta del saldo disponible lo pendiente y lo confirmado sin REP. En PostgreSQL se bloquean las facturas en orden estable dentro de una transacción para serializar propuestas concurrentes. |
| D13, depósito excedente | Se conserva y muestra como saldo a favor por identificar del cliente. En la propuesta pendiente se distingue de un saldo confirmado; al rechazar desaparece esa indicación vigente. Facturación recibe el importe bancario y el excedente por separado; el REP excluye el excedente. |
| Pagos a cuenta | Proveedor obligatorio. El control y el índice incluyen `NoAplicado` y `AplicadoParcial`. Desligar tiene comando y ruta propios: guarda motivo, revierte la aplicación, recalcula estado y restituye el pasivo, sin crear movimiento bancario. Permite religar conservando historia. |
| Reversa | Motivo persistido en la aplicación y el contramovimiento, expuesto en detalle. El pago a cuenta utiliza desligar, no la reversa bancaria de un pago ordinario. |
| Cuentas | Sucursal, finalidad, titular y firmantes. Saldo inicial con corte, motivo, permiso de administración, concurrencia y bitácora. Captura única. Duplicados con número enmascarado; CLABE consultada según permiso. |
| D17, flujo | Catálogo editable con alta, edición, baja lógica y reactivación; operación, inversión y financiamiento. Se reutilizaron los 13 ejemplos del seed y se identifican como ejemplos por validar con Millet. Selectores en ingreso, pago y pago a cuenta; reclasificación con motivo y bitácora. |
| Saldos y monedas | Flujo y auxiliar utilizan saldo inicial. Flujo presenta filas inicial/final por cuenta y totales por moneda, también para exportación. Movimientos sin concepto se muestran sin clasificar. Las cuentas sin saldo capturado se identifican como pendientes. |

## Decisiones y diferencias menores respecto a la ficha

- Se continuó el trabajo existente y se corrigieron fixtures y expectativas obsoletas; no se reemplazó el paquete desde cero.
- El seed ya incluía subconceptos bajo las tres clasificaciones. Se reutilizó en lugar de duplicarlo.
- Se eligió captura única del saldo inicial, alternativa explícitamente permitida por la ficha. El corte debe ser anterior al primer movimiento; los nuevos movimientos deben ser posteriores al corte. Así se evita contar importes dos veces. No se implementó ajuste posterior del saldo.
- Se extendió el contrato espejo de Facturación para descontar el saldo a favor del REP. Es necesario para D13 y no cambia el REPP del proveedor de P4.
- Los eventos se publican antes de `SaveChanges`, mediante el Outbox existente. No se modificaron CxP, Compras, infraestructura ni archivos de entorno.

## Migraciones P5

- `20261009154847_P5CobranzaConfirmacionTesoreria`: proponente, movimiento confirmado, indicador REP y saldo a favor en CxC.
- `20261009154918_P5TesoreriaCompleta`: inventario y saldo inicial de cuentas, motivos, proponente y excedente del depósito, ejemplos del catálogo e índices de pagos a cuenta/aplicaciones activas.

La migración de Tesorería se detiene con un mensaje en español si encuentra varios pagos a cuenta abiertos para el mismo proveedor. No concilia ni borra movimientos automáticamente. No se aplicaron migraciones a ninguna base.

Las propuestas históricas sin proponente no se atribuyen a una persona inventada: Tesorería rechaza su confirmación con un mensaje que pide rechazo y nueva propuesta. Las confirmaciones históricas deben revisarse si no tienen asociación con el movimiento/REP; no se deduce su estado fiscal en una migración.

## Validación

Resultados finales de esta continuación, con los ensamblados y fuentes actuales.

| Comprobación | Resultado verificado |
| --- | --- |
| `dotnet build Millet.sln` | 0 errores, 0 advertencias. Se usó un nodo y compilación sin servidor de MSBuild por restricciones del sandbox. |
| Tesorería, xUnit | 119 pruebas, 0 fallos, 0 omitidas. |
| CxC, xUnit | 120 pruebas, 0 fallos, 0 omitidas. |
| Facturación, xUnit | 398 pruebas, 0 fallos, 0 omitidas. |
| `npx tsc --noEmit -p tsconfig.json` | Código de salida 0. |
| `npm run -s typecheck:test` | Código de salida 0. |
| `npm run -s lint` | 0 errores; 10 advertencias de hooks en Administración, fuera del paquete. |
| `npx vitest run --maxWorkers=1 --testTimeout=15000` | 340 suites y 1,993 pruebas aprobadas. Código de salida 0. Duración: 666.63 s. |
| `dotnet ef migrations has-pending-model-changes` | Sin cambios de modelo pendientes, tanto en Tesorería como en CxC. No abre ni migra una base. |
| Integración PostgreSQL | Escrita y compilada; no ejecutada en este sandbox sin Docker disponible para la tarea. |

El `dotnet test` convencional aborta al abrir el socket de VSTest (`SocketException (13): Permission denied`). Se ejecutaron los ensamblados actuales con `Xunit.Runners.AssemblyRunner`, en proceso, usando un ejecutor temporal fuera del repositorio. Esto verifica las pruebas xUnit; no demuestra que VSTest ni las integraciones funcionen en este entorno.

Logs de esta continuación: `/tmp/p5-continuacion-09oct/` (`build-final.log`, `unit-verificado.log`, `dotnet-test-standard.log`, `tsc.log`, `typecheck-test.log`, `lint.log`, `vitest-final.log`, `modelo-tesoreria.log`, `modelo-cxc.log`). Los logs de corridas interrumpidas no se presentan como resultados finales.

Vitest se ejecutó completo, sin filtros, con un trabajador y 15 segundos de timeout por prueba, por carga alta de la máquina. No se cambió su configuración en el repositorio. Las corridas iniciales con más trabajadores se interrumpieron después de timeouts; el fallo del smoke de CxC que esperaba los botones retirados se corrigió antes de la corrida final. jsdom imprime avisos de navegación y canvas no implementados, sin fallos en el resultado final.

## Pruebas añadidas o ajustadas

- Tesorería: segregación de quien propone/confirma; R4b, un proveedor por pago; pasivo no autorizado; parcial abierto; proveedor requerido; desligar sin ingreso y religar; motivo de reversa; cuenta duplicada enmascarada; CLABE con/sin permiso; permiso y corte del saldo inicial; reapertura incompatible con otro pago abierto; flujo, reclasificación y monedas.
- CxC: excedente, reservas pendientes y confirmadas sin REP, rechazo y nueva propuesta, confirmación idempotente, proponente impedido y liberación tras REP sin doble aplicación.
- Facturación: depósito de 1,100 con 100 de saldo a favor genera pendiente de REP por 1,000.
- Frontend: cálculo puro del excedente, selector accesible, reclasificación con motivo/versión, saldo a favor pendiente y ausencia al rechazar, ausencia de confirmar/rechazar CxC. Se actualizó el smoke que esperaba los botones retirados.
- API/PostgreSQL: super-admin proponente 422 y ausencia de aplicación/evento; confirmación/rechazo con Outbox y resolución CxC; rutas CxC retiradas; propuestas concurrentes; 400 sin proveedor y 422 con parcial; saldo/corte/reclasificación/bitácora/flujo; desligar por endpoint sin contramovimiento; catálogo y baja lógica. Los datos de catálogo creados por estas pruebas se limpian; las demás usan el seed.

## Pendientes y siguiente acción

1. Claude debe ejecutar `bash tools/validate-integration-isolated.sh` **completo**, contra PostgreSQL desechable, y conservar rojo/verde. No basta ejecutar solamente el filtro P5.
2. Configurar/verificar fuera de este paquete la suscripción `cuentas-por-cobrar-tesoreria-sub` del topic `tesoreria-events`, con entrega de `tesoreria.pago-cliente.confirmado.v1` y `tesoreria.propuesta-aplicacion.rechazada.v1`. El consumidor está registrado cuando existe configuración de Service Bus; la suscripción no figura en la infraestructura revisada del repo. Su existencia en Azure y el recorrido real están **Por confirmar**.
3. Revisión visual y recorrido con dos personas: proponer, impedir autoconfirmación, confirmar desde Tesorería con banco, consumir evento, emitir REP y comprobar cartera/reservas; repetir con rechazo y nueva propuesta. El frontend fue probado en jsdom; no se levantó servidor ni se realizó aceptación visual.
4. Aplicar migraciones en el entorno de validación y revisar datos históricos antes de prueba del lunes. Validar con Millet el catálogo de ejemplo y capturar saldos con cortes documentados.
5. Actualizar la bóveda solo después de revisión/autorización del borrador local adjunto. No marcar funciones aceptadas con esta evidencia local.

Commit propuesto (no creado): `fix(tesoreria): completar cobranza y controles del paquete P5`.

## Archivos del paquete en este worktree

Inventario de cambios locales al cierre; incluye el trabajo recuperado de la corrida anterior.

```text
backend/src/Api/Endpoints/CuentasPorCobrar/PropuestasAplicacionEndpoints.cs
backend/src/Api/Endpoints/Tesoreria/ConceptosEndpoints.cs
backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs
backend/src/Api/Endpoints/Tesoreria/MovimientosEndpoints.cs
backend/src/Api/Endpoints/Tesoreria/PagosACuentaEndpoints.cs
backend/src/Api/Program.cs
backend/src/CuentasPorCobrar/Application/AplicacionPagos/PropuestasAplicacionCommands.cs
backend/src/CuentasPorCobrar/Application/EventListeners/ContratosEspejo.cs
backend/src/CuentasPorCobrar/Application/EventListeners/ReciboPagoTimbradoCommand.cs
backend/src/CuentasPorCobrar/Application/EventListeners/ResolverPropuestaTesoreriaCommand.cs
backend/src/CuentasPorCobrar/Application/Integration/CuentasPorCobrarIntegrationEvents.cs
backend/src/CuentasPorCobrar/Domain/AplicacionPagos/PropuestaAplicacionPago.cs
backend/src/CuentasPorCobrar/Infrastructure/Persistence/Configurations/PropuestaAplicacionConfigurations.cs
backend/src/CuentasPorCobrar/Infrastructure/Persistence/Migrations/20261009154847_P5CobranzaConfirmacionTesoreria.Designer.cs
backend/src/CuentasPorCobrar/Infrastructure/Persistence/Migrations/20261009154847_P5CobranzaConfirmacionTesoreria.cs
backend/src/CuentasPorCobrar/Infrastructure/Persistence/Migrations/CuentasPorCobrarDbContextModelSnapshot.cs
backend/src/CuentasPorCobrar/Infrastructure/Workers/TesoreriaEventListenerWorker.cs
backend/src/Facturacion/Application/EventListeners/ContratosEspejoTesoreria.cs
backend/src/Facturacion/Application/EventListeners/EmitirReppDesdePagoConfirmadoCommand.cs
backend/src/Tesoreria/Application/Cuentas/ConceptosCommands.cs
backend/src/Tesoreria/Application/Cuentas/CuentasCommands.cs
backend/src/Tesoreria/Application/Cuentas/RegistrarSaldoInicialCommand.cs
backend/src/Tesoreria/Application/Cuentas/SaldosPorCuentaQuery.cs
backend/src/Tesoreria/Application/Depositos/DepositosCommands.cs
backend/src/Tesoreria/Application/EventListeners/ContratosEspejoCxc.cs
backend/src/Tesoreria/Application/EventListeners/ProyectarPropuestaAplicacionCommand.cs
backend/src/Tesoreria/Application/Integration/TesoreriaIntegrationEvents.cs
backend/src/Tesoreria/Application/Movimientos/MovimientosQueries.cs
backend/src/Tesoreria/Application/Movimientos/ReclasificarMovimientoCommand.cs
backend/src/Tesoreria/Application/Pagos/PagosCommands.cs
backend/src/Tesoreria/Application/PagosACuenta/DesligarPagoACuentaCommand.cs
backend/src/Tesoreria/Application/PagosACuenta/PagosACuentaCommands.cs
backend/src/Tesoreria/Application/Reportes/AuxiliarBancosReporteQuery.cs
backend/src/Tesoreria/Application/Reportes/FlujoEfectivoReporteQuery.cs
backend/src/Tesoreria/Domain/Cuentas/ConceptoMovimiento.cs
backend/src/Tesoreria/Domain/Cuentas/CuentaBancaria.cs
backend/src/Tesoreria/Domain/Depositos/DepositoConfirmacion.cs
backend/src/Tesoreria/Domain/Movimientos/AplicacionPagoProveedor.cs
backend/src/Tesoreria/Domain/Movimientos/MovimientoBancario.cs
backend/src/Tesoreria/Infrastructure/Persistence/Configurations/ConceptoMovimientoConfiguration.cs
backend/src/Tesoreria/Infrastructure/Persistence/Configurations/CuentaBancariaConfiguration.cs
backend/src/Tesoreria/Infrastructure/Persistence/Configurations/DepositoConfirmacionConfiguration.cs
backend/src/Tesoreria/Infrastructure/Persistence/Configurations/MovimientoBancarioConfiguration.cs
backend/src/Tesoreria/Infrastructure/Persistence/Migrations/20261009154918_P5TesoreriaCompleta.Designer.cs
backend/src/Tesoreria/Infrastructure/Persistence/Migrations/20261009154918_P5TesoreriaCompleta.cs
backend/src/Tesoreria/Infrastructure/Persistence/Migrations/TesoreriaDbContextModelSnapshot.cs
backend/tests/Api.IntegrationTests/Tesoreria/P5TesoreriaCobranzaTests.cs
backend/tests/CuentasPorCobrar.UnitTests/AplicacionPagos/PropuestaAplicacionTests.cs
backend/tests/Facturacion.UnitTests/Repp/ReppPendienteTests.cs
backend/tests/Tesoreria.UnitTests/Depositos/DepositoConfirmacionTests.cs
backend/tests/Tesoreria.UnitTests/Depositos/DepositosListenerHandlersTests.cs
backend/tests/Tesoreria.UnitTests/Movimientos/PagoACuentaDomainTests.cs
backend/tests/Tesoreria.UnitTests/Movimientos/PagoProveedorDomainTests.cs
backend/tests/Tesoreria.UnitTests/Pagos/P5ReglasHandlersTests.cs
backend/tests/Tesoreria.UnitTests/Reportes/ReportesQueriesTests.cs
docs/entrega/P5-borrador-actualizacion-boveda-09oct.md
docs/entrega/P5-tesoreria-cobranza-09oct.md
frontend/src/features/cxc/api/types.ts
frontend/src/features/cxc/api/useAplicaciones.test.tsx
frontend/src/features/cxc/api/useAplicaciones.ts
frontend/src/features/cxc/lib/diferencia-deposito.test.ts
frontend/src/features/cxc/lib/diferencia-deposito.ts
frontend/src/features/cxc/pages/BandejaAplicaciones.smoke.test.tsx
frontend/src/features/cxc/pages/DetalleAplicacion.tsx
frontend/src/features/cxc/pages/NuevaPropuestaAplicacion.tsx
frontend/src/features/tesoreria/api/types.ts
frontend/src/features/tesoreria/api/useTesoreria.ts
frontend/src/features/tesoreria/components/CatalogoConceptos.tsx
frontend/src/features/tesoreria/components/ConceptoSelector.tsx
frontend/src/features/tesoreria/components/ConfirmarDepositoDialog.tsx
frontend/src/features/tesoreria/components/CuentaBancariaSheet.tsx
frontend/src/features/tesoreria/components/NuevoPagoACuentaSheet.tsx
frontend/src/features/tesoreria/components/P5Controles.test.tsx
frontend/src/features/tesoreria/components/ReclasificarMovimiento.tsx
frontend/src/features/tesoreria/components/RegistrarPagoSheet.tsx
frontend/src/features/tesoreria/pages/DetalleMovimiento.tsx
frontend/src/features/tesoreria/pages/ReporteFlujoEfectivoPage.tsx
```
