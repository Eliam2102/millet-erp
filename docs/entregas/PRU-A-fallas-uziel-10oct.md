# PRU-A · Correcciones de las fallas de Uziel del 10-oct

Fecha: 11-oct-2026. Worktree: `millet_erp-PRU-A`. Rama: `fix/PRU-A-fallas-prueba-uziel`.
Base verificada al iniciar: `e1aa6684`; árbol inicialmente limpio.

Estado: cambios locales implementados y compilados; pruebas unitarias verificadas. La puerta de integración PostgreSQL y la repetición con los perfiles reales de Millet están **Por confirmar**. No hay commit ni push, conforme a la regla final del paquete.

Fuente de reproducción: `outputs/Codex_F1/PRU-REPETIR-PRUEBAS-uziel-10oct.md` del directorio padre, leído antes de implementar. Se leyeron AGENTS, CLAUDE, CONTRIBUTING, instrucciones del frontend, DESIGN y ADR-0051. Las observaciones de PRU-B quedan fuera de este cambio.

## Causas verificadas, cambios y cobertura

| Casos | Causa en el código actual | Corrección | Prueba de regresión |
|---|---|---|---|
| TES-03 / FAC-13 | Tesorería y Facturación resolvían el puerto de periodos con NoOp. El REP revisaba el periodo de emisión, sin revisar la fecha del pago. | Adaptadores sobre el puerto público de Contabilidad; validación de fecha fiscal mexicana del pago, incluida la ruta desde pendientes y el reintento. Se conserva el candado de la reversa. | `TES03_periodo_real_rechaza_ingreso_pago_pago_a_cuenta_y_reversa_y_octubre_pasa` y `FAC13_REP_manual_y_pendiente_rechazan_septiembre_cerrado_y_octubre_timbra`. |
| CXP-12 | La entidad conservaba el offset −06:00 recibido; PostgreSQL requiere UTC para timestamptz. | Normalización al construir la nota de crédito y revisión de otros campos y filtros de fecha. | `CXP12_captura_NC_con_offset_Mexico_y_persiste_el_mismo_instante_en_UTC`: fecha explícita −06:00, mismo instante y offset cero al releer. |
| CXC-09 | El mapping FiscalAPI enviaba el monto y los documentos relacionados con la escala original de seis decimales. | Precisión del catálogo de monedas; redondeo del pago y documentos, distribución del residuo y saldo insoluto derivado de anterior menos pagado. | `CXC09_REP_desde_pendientes_pasa_mapping_SDK_de_dos_decimales_y_libera_saldo`, MXN y USD; prueba SDK con dos documentos y residuo de un centavo. |
| CXC-04 | El alta exigía gestión de todas las sucursales; los ingresos manuales carecían de sucursal para resolver su visibilidad. El frontend guardaba el ingreso antes de confirmar. El selector usaba saldos restringidos por todos los movimientos de la cuenta. | Alcance operativo por sucursal; columna nullable y migración; ingreso y confirmación en un solo SaveChanges con Outbox. Acción «Registrar ingreso». Catálogo para ingresos con cuentas activas, número enmascarado y sin saldos. | `CXC04_ingreso_de_otro_usuario_de_MID_es_visible_y_operativo_no_accede_a_otra_sucursal`; `CXC04_alta_rapida_y_confirmacion_son_atomicas_incluso_si_proponente_intenta_confirmar`, éxito y rechazo; pruebas de validación y selector del frontend. |
| TES-15 | El selector requería acceso general a CFDI de CxP y los CFDI de pago no tenían una factura única para resolver el alcance. El registro manual exige XML, por lo que UUID solo no permite registrar. | Consulta y descarga para el permiso propio de Tesorería, revisando cada UUID relacionado en el XML. Se excluyen documentos desconocidos o de sucursales ajenas, incluidos XML mixtos. El formulario explica que se requiere XML. | `TES15_tesorero_MID_vincula_XML_de_REP_sin_permiso_CxP_y_no_ve_MTY_ni_XML_mixto`: selección, XML, pago y registro del REP con Outbox. |
| TES-16 | Solo el escucha de pagos de Tesorería publicaba a Compras; el acumulado sumaba exclusivamente pagos monetarios. | Anticipos, cargos y notas de crédito publican el saldo cubierto antes de SaveChanges. El publicador suma las cuatro formas de liquidar y Compras deduplica por aplicación. | `TES16_anticipo_cargo_y_pago_publican_acumulado_en_Outbox_y_Compras_cierra_sin_duplicar_aplicaciones`: 300 de anticipo + 200 de cargo + 660 de pago, acumulado 1,160 y OC Cerrada. |
| FAC-15 | Reintentar reutilizaba el snapshot del emisor capturado antes de corregir Empresa. | Reconstrucción del emisor desde empresa y sucursal vigentes únicamente en TimbradoFallido; conserva folio y rechaza modificar lo timbrado. | `FAC15_reintento_usa_emisor_corregido_guarda_mismo_folio_y_lo_timbrado_no_se_modifica`: PAC de prueba rechaza nombre incorrecto, corrección del maestro, reintento y persistencia. También unitarias de Facturación. |
| AW-02 / AW-03 / CXC-08 / TES-08, kit | Faltaban banderas, equivalencias y sucursal en el arranque. La emisión por lote ocultaba el motivo del fallo. El seed restablecía razón social y permisos, y cerraba con fecha fija futura. | Defaults públicos en demo.sh; aviso con motivo tanto por lote como individual; seed crea empresa y permisos iniciales solo cuando faltan, conserva ediciones y usa el reloj real para cierre. | Se amplió `DemoSesionSeedTests.DosArranques_NoDuplican_YConservanEstadosAdjuntosYUsuarios`: razón social editada, permiso agregado y otro retirado sobreviven; cierre sin fecha futura. Pruebas puras del aviso. |

Las once ejecuciones del conjunto PRU-A están en `backend/tests/Api.UnitTests/PruA/PruARegressionTests.cs`. El mismo archivo se compila en Api.IntegrationTests con `P3_POSTGRES`: usa Npgsql, adaptadores reales de periodos, handlers y Outbox; no abre una aplicación ni activa login. Los casos unitarios usan InMemory. Por una limitación de EF10/InMemory con ComplexProperty, el caso de Compras aplica el evento al agregado cargado; la variante PostgreSQL usa el escucha real. La prueba del seed usa su base desechable propia y conserva la infraestructura de pruebas existente.

La prueba de metadatos `PruAMigracionTests` compara snapshot y modelo Npgsql sin abrir conexión: la migración de `MovimientoBancario.SucursalId` coincide con el modelo. Se ajustó el fixture anterior de saldo inicial/reclasificación P5 para declarar octubre abierto: ese caso dependía del NoOp; los nuevos casos PRU-A conservan el adaptador real. Las pruebas no agregan filas persistentes a catálogos compartidos; emplean empresa propia, transacciones o limpieza por empresa.

## Fechas corregidas a UTC

Se conserva el instante; las fechas fiscales del REP y Carta Porte se vuelven a expresar en la zona fiscal mexicana al construir el payload, para evitar desplazar seis horas la fecha enviada. El formulario de REP conserva el día local al precargarlo.

- CxP: nota de crédito (`FechaCfdi`); anticipo (`FechaCfdi`); CFDI recibido (`FechaCfdi`, `FechaRecepcion`); factura (`FechaDocumento`, `FechaContabilizacion`, incluida edición); comprobación de gastos (`FechaCfdi`); comprobación de viáticos (`FechaGasto`). La captura con tarjeta pasa por la misma construcción de factura normalizada.
- Compras: requisición (`FechaSolicitud`) y línea de orden (`FechaEntregaLinea`, alta y edición).
- CxC: seguimiento de cobranza (`Fecha`) y factura de cartera (`FechaTimbrado`, `FechaVencimiento`).
- Facturación: recibo de pago (`FechaPago`), Carta Porte (`FechaSalida`, `FechaLlegadaEstimada`), filtros Desde/Hasta de Control de anticipos y Liquidación de caja.
- Integración A+W: filtros Desde/Hasta de cotizaciones (`SubmittedAt`).

Las fechas de Inicio ya estaban convertidas a UTC y se reutilizaron. Los demás campos revisados provenían del reloj UTC, del parser normalizado, de eventos internos del reloj o eran DateOnly; no se cambió el tipo de cambio.

## Verificación local

Compilación de `backend/Millet.sln`: **0 errores, 0 advertencias** (última ejecución: 9.93 s). Se restauraron paquetes desde la caché local, sin cambiar referencias de paquetes.

VSTest no puede abrir su socket en este sandbox (`SocketException (13): Permission denied` al iniciar TcpListener). Las suites se ejecutaron con el runner de xUnit en el mismo proceso, desde sus directorios bin y con sus dependencias completas. Esta ejecución verificó los asserts reales; no sustituye la puerta PostgreSQL.

| Suite unitaria | Pasaron | Fallaron / omitidas |
|---|---:|---:|
| Tesorería | 132 | 0 / 0 |
| Facturación | 454 | 0 / 0 |
| Cuentas por pagar | 446 | 0 / 0 |
| Integraciones.Fiscal | 206 | 0 / 0 |
| Compras | 578 | 0 / 0 |
| Cuentas por cobrar | 121 | 0 / 0 |
| Integraciones.Aw | 339 | 0 / 0 |
| API, incluidas regresiones PRU-A y metadatos de migración | 66 | 0 / 0 |
| **Total** | **2,342** | **0 / 0** |

- `npx tsc --noEmit -p tsconfig.json`: código de salida 0.
- `npm run -s typecheck:test`: código de salida 0.
- `npm run -s lint`: código de salida 0; cero errores y diez advertencias previas en Administración.
- Suite completa `npx vitest run`: **377 archivos y 2,156 pruebas pasaron**, cero fallas (139.83 s).
- `bash -n tools/demo/demo.sh` y `git diff --check`: código de salida 0.

Los registros de verificación local están en `/tmp/prua-build-delivery.log`, `/tmp/prua-unit-final-<suite>.log`, `/tmp/prua-tsc-last.log`, `/tmp/prua-typecheck-complete.log`, `/tmp/prua-lint-complete.log` y `/tmp/prua-vitest-last.log`.

## Pendientes y siguiente ejecución

1. Claude debe ejecutar `tools/validate-integration-isolated.sh` completo, con Docker y PostgreSQL desechable. Los casos nuevos compilan pero sus resultados PostgreSQL, incluida la idempotencia del seed, están **Por confirmar**. No se modificó ni ejecutó ese script en este sandbox.
2. Repetir los casos de Uziel con los perfiles reales de Entra y el PAC de pruebas. Verificar en la demo la orden `OC-DEMO2026-000102`. La regresión usa una orden ficticia aislada `OC-FIX2026-000102`; no acredita el estado de la orden ya existente de demo ni un timbrado real.
3. `.env.demo.example` no se modificó porque la regla final prohíbe tocar `.env*`. El bloque exacto propuesto está en `tools/demo/README.md`; los defaults operativos ya están en `demo.sh`. Incorporar el bloque a la plantilla requiere levantar esa restricción.
4. CXC-07 queda documentado: REP manual desde bandeja, U1.1 / #52. Las observaciones corresponden a PRU-B.

No se levantaron servidores en 5080/5173, no se activó el login simulado, no se tocaron infra ni archivos .env y no hubo escrituras a tareas, mensajes, documentos externos o datos de otra empresa.

Mensaje de commit propuesto, sin ejecutarlo:

`fix(pru-a): corregir fallas de tesorería y facturación y preservar la demo`

## Archivos cambiados

- `backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs`
- `backend/src/Api/Endpoints/Tesoreria/DepositosEndpoints.cs`
- `backend/src/Api/Endpoints/Tesoreria/MovimientosEndpoints.cs`
- `backend/src/Api/Endpoints/Tesoreria/ReppEndpoints.cs`
- `backend/src/Api/Seed/DemoSesionFinanzas.cs`
- `backend/src/Api/Seed/DemoSesionMaestros.cs`
- `backend/src/Api/Seed/DemoSesionUsuarios.cs`
- `backend/src/Compras/Application/Oc/Eventos/Cxp/ContratosEspejoCxp.cs`
- `backend/src/Compras/Application/Oc/Eventos/Cxp/FacturaPagoAplicadoCommand.cs`
- `backend/src/Compras/Domain/Oc/LineaOrdenCompra.cs`
- `backend/src/Compras/Domain/Requisicion.cs`
- `backend/src/Contabilidad/Application/PublicPorts/IPeriodoContableConsultaPort.cs`
- `backend/src/CuentasPorCobrar/Domain/Cartera/FacturaCartera.cs`
- `backend/src/CuentasPorCobrar/Domain/Cobranza/SeguimientoCobranza.cs`
- `backend/src/CuentasPorPagar/Application/Cfdi/ListarCfdis/ListarReppParaTesoreriaQuery.cs`
- `backend/src/CuentasPorPagar/Application/EventListeners/Tesoreria/TesoreriaEventListenerCommands.cs`
- `backend/src/CuentasPorPagar/Application/FacturaProveedor/AplicarAnticipo/AplicarAnticipoAFacturaCommand.cs`
- `backend/src/CuentasPorPagar/Application/FacturaProveedor/AplicarNotaCredito/AplicarNotaCreditoAFacturaCommand.cs`
- `backend/src/CuentasPorPagar/Application/Integration/FacturaPagoAplicadoIntegrationEvent.cs`
- `backend/src/CuentasPorPagar/Application/Integration/Mappers/FacturaPagoAplicadoMapper.cs`
- `backend/src/CuentasPorPagar/Application/NotaCargo/NotaCargoCommands.cs`
- `backend/src/CuentasPorPagar/Application/NotaCreditoProveedor/CapturarNotaCredito/CapturarNotaCreditoCommand.cs`
- `backend/src/CuentasPorPagar/Domain/AnticipoProveedor/AnticipoProveedor.cs`
- `backend/src/CuentasPorPagar/Domain/Cfdi/CfdiRecibido.cs`
- `backend/src/CuentasPorPagar/Domain/ComprobacionGastos/LineaComprobacionGastos.cs`
- `backend/src/CuentasPorPagar/Domain/FacturaProveedor/Events/FacturaProveedorPagoAplicadoDomainEvent.cs`
- `backend/src/CuentasPorPagar/Domain/FacturaProveedor/FacturaProveedor.cs`
- `backend/src/CuentasPorPagar/Domain/NotaCreditoProveedor/NotaCreditoProveedor.cs`
- `backend/src/CuentasPorPagar/Domain/Viaticos/LineaComprobacionViaticos.cs`
- `backend/src/CuentasPorPagar/Infrastructure/DependencyInjection.cs`
- `backend/src/Facturacion/Application/Reportes/ControlAnticipos/ControlAnticiposResumenQuery.cs`
- `backend/src/Facturacion/Application/Reportes/LiquidacionCaja/LiquidacionCajaQuery.cs`
- `backend/src/Facturacion/Application/Repp/EmitirRepp/EmitirReppHandler.cs`
- `backend/src/Facturacion/Application/Timbrado/CfdiEmisionBuilder.cs`
- `backend/src/Facturacion/Application/Timbrado/ReintentarTimbrado/ReintentarTimbradoHandler.cs`
- `backend/src/Facturacion/Domain/CartaPorte/CartaPorte.cs`
- `backend/src/Facturacion/Domain/Comprobantes/Comprobante.cs`
- `backend/src/Facturacion/Domain/Ports/IEmpresaFiscalReadPort.cs`
- `backend/src/Facturacion/Domain/Ports/IPeriodoContablePort.cs`
- `backend/src/Facturacion/Domain/Repp/ReciboPago.cs`
- `backend/src/Facturacion/Infrastructure/Adapters/PeriodoContableAdapter.cs`
- `backend/src/Facturacion/Infrastructure/Catalogos/ReppBancarioReadAdapter.cs`
- `backend/src/Facturacion/Infrastructure/DatosMaestros/EmpresaFiscalReadAdapter.cs`
- `backend/src/Facturacion/Infrastructure/DependencyInjection.cs`
- `backend/src/Facturacion/Infrastructure/Stubs/NoOpReadPorts.cs`
- `backend/src/Facturacion/Millet.Facturacion.csproj`
- `backend/src/Integraciones.Aw/Application/Queries/ListarCotizaciones/ListarCotizacionesHandler.cs`
- `backend/src/Integraciones.Fiscal/Domain/Ports/IMonedaPrecisionReadPort.cs`
- `backend/src/Integraciones.Fiscal/Infrastructure/DependencyInjection.cs`
- `backend/src/Integraciones.Fiscal/Infrastructure/SdkAdapter/FiscalApiTimbradoAdapter.cs`
- `backend/src/Integraciones.Fiscal/Infrastructure/SdkAdapter/MonedaPrecisionReadAdapter.cs`
- `backend/src/Integraciones.Fiscal/Millet.Integraciones.Fiscal.csproj`
- `backend/src/Tesoreria/Application/Cuentas/ListarCuentasParaIngresosQuery.cs`
- `backend/src/Tesoreria/Application/Depositos/DepositosCommands.cs`
- `backend/src/Tesoreria/Application/Movimientos/RegistrarMovimientoIngresoCommand.cs`
- `backend/src/Tesoreria/Application/Pagos/PagosCommands.cs`
- `backend/src/Tesoreria/Domain/Movimientos/MovimientoBancario.cs`
- `backend/src/Tesoreria/Domain/Ports/IPeriodoContablePort.cs`
- `backend/src/Tesoreria/Infrastructure/Adapters/PeriodoContableAdapter.cs`
- `backend/src/Tesoreria/Infrastructure/DependencyInjection.cs`
- `backend/src/Tesoreria/Infrastructure/Persistence/Migrations/20261011120000_IngresoSucursal.Designer.cs`
- `backend/src/Tesoreria/Infrastructure/Persistence/Migrations/20261011120000_IngresoSucursal.cs`
- `backend/src/Tesoreria/Infrastructure/Persistence/Migrations/TesoreriaDbContextModelSnapshot.cs`
- `backend/src/Tesoreria/Infrastructure/Stubs/NoOpPeriodoContablePort.cs`
- `backend/src/Tesoreria/Infrastructure/TesoreriaSucursalReadAdapter.cs`
- `backend/tests/Api.IntegrationTests/Administracion/DemoSesionSeedTests.cs`
- `backend/tests/Api.IntegrationTests/Millet.Api.IntegrationTests.csproj`
- `backend/tests/Api.IntegrationTests/Tesoreria/P5TesoreriaCobranzaTests.cs`
- `backend/tests/Api.UnitTests/Millet.Api.UnitTests.csproj`
- `backend/tests/Api.UnitTests/PruA/PruAMigracionTests.cs`
- `backend/tests/Api.UnitTests/PruA/PruARegressionTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/FacturaProveedor/P3FacturaConOcTests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/P4/SaldosP4Tests.cs`
- `backend/tests/CuentasPorPagar.UnitTests/P8/AplicacionesP8Tests.cs`
- `backend/tests/Facturacion.UnitTests/Timbrado/ReintentarTimbradoHandlerTests.cs`
- `backend/tests/Integraciones.Fiscal.UnitTests/Infrastructure/FiscalApiTimbradoAdapterTests.cs`
- `docs/entregas/PRU-A-fallas-uziel-10oct.md`
- `frontend/src/features/cxp/api/useCfdis.ts`
- `frontend/src/features/cxp/components/CfdiPorProcesarPicker.tsx`
- `frontend/src/features/facturacion/lib/repp-pendiente.test.ts`
- `frontend/src/features/facturacion/lib/repp-pendiente.ts`
- `frontend/src/features/facturacion/pages/PendientesRepp.tsx`
- `frontend/src/features/tesoreria/api/keys.ts`
- `frontend/src/features/tesoreria/api/useTesoreria.ts`
- `frontend/src/features/tesoreria/components/ConfirmarDepositoDialog.tsx`
- `frontend/src/features/tesoreria/components/CuentaBancariaSelector.test.tsx`
- `frontend/src/features/tesoreria/components/CuentaBancariaSelector.tsx`
- `frontend/src/features/tesoreria/components/RegistrarIngresoSheet.tsx`
- `frontend/src/features/tesoreria/components/RegistrarReppSheet.tsx`
- `frontend/src/features/tesoreria/lib/ingreso.test.ts`
- `frontend/src/features/tesoreria/lib/ingreso.ts`
- `frontend/src/features/tesoreria/pages/BandejaMovimientos.tsx`
- `frontend/src/lib/datetime.ts`
- `frontend/src/lib/fecha-local-iso.test.ts`
- `tools/demo/README.md`
- `tools/demo/demo.sh`
