# Tasks: entrega verificable de servicios CE

Input: spec.md, plan.md, research.md, data-model.md, contracts/flujo-servicio.md.
Tests: requeridas para lógica de autorización/dinero y demostración de flujo. Ejecución autorizada por Eliam: reutilizar, integrar con develop y probar el circuito completo antes de QA. [x] sólo evaluación ya comprobada; el código previo no vuelve a contarse como construido aquí.

## Phase 1: Setup
- [x] T001 Fijar develop954a52c7a y ramaaislada; resolver templates oficiales en specs/002-ce-servicios-entrega/ sin modificar specs/001-n15-cierre-ce/.
- [x] T002 Verificar por huella los74archivos y parche de outputs/COMEX_AF_listo_pruebas/Codigo_local.json; realizar check3vías con índice temporal y conservar evidencia fuera delrepo.

## Phase 2: Foundational
- [x] T003 Integrar el paquete local reutilizado en rama de implementación basada en developfresco; revisar backend/src/Api/Program.cs, infra/modules/servicebus.bicep y permisosbackend/frontend preservando lotes entrantes.
- [x] T004 Reconciliar las3migraciones/snapshots en backend/src/{Compras,CuentasPorPagar,Identidad}/Infrastructure/ y ensayar actualización/históricos; no asignar roles reales.
- [x] T005 Preservar helper backend/src/Compras/Application/Listar/PendientesAutorizacionLectura.cs y extender consumidores listadoOC/ComprasFuenteAvisos coherentes con facultadesCE; no restaurar filtroduplicado antiguo.

## Phase 3: US1 · Servicio a crédito
Goal: N1→pasivo→N2→Tesorería con unafactura.
Independent test: circuitoHTTPcompleto sin insertar la factura entre firmas.
- [x] T006 [US1] Reutilizar guards/dominio/captura/liberación preparados en backend/src/Compras/Application/Oc/FirmasServicioCeGuard.cs y backend/src/CuentasPorPagar/Application/FacturaProveedor/; no reconstruirlos.
- [x] T007 [US1] Añadir circuitoHTTPreal a backend/tests/Api.IntegrationTests/ComercioExterior/FirmasServiciosHttpTests.cs con capturaCxP y liberaciónHTTP; comprobar una deuda y efectoTesorería.
- [x] T008 [US1] Añadir regresión cancelación/sustitución de factura despuésN2 en backend/tests/Api.IntegrationTests/ComercioExterior/FirmasServiciosHttpTests.cs y P3FacturaConOcTests.cs; probar vigencia y reintento.
- [x] T009 [US1] Si T008 falla, corregir sólo guarda/vigencia compartida en backend/src/Api/Infrastructure/Adapters/FacturaServicioCeReadAdapter.cs, Compras/Oc/Autorizar y CxP/AutorizarFactura; conservar identidad/historia y no inventar regla adicional.
- [x] T010 [US1] Ejecutar negativos de rol/personas/empresa/sucursal/duplicado/incompleta/históricos y comprasordinarias en backend/tests/{Compras,CuentasPorPagar}.UnitTests y Api.IntegrationTests/ComercioExterior/.
- [x] T011 [US1] Verificar indicador/etiquetasN1/N2 y Enviar aTesorería con UI existente en frontend/src/features/compras/ordenes/ y frontend/src/features/cxp/pages/FacturaDetallePage.p3.test.tsx; incluir bandeja/avisos afectadosporT005.

## Phase 4: US2 · Servicios pagados y cierre
Goal: no cerrarIMPO con deuda, reversa o lectura no verificable.
Independent test: impago→40pagado→100pagado→reversa y proyecciónpendiente por flujo deTesorería.
- [x] T012 [US2] Reutilizar backend/src/ComercioExterior/Application/CierreServiciosCeServicio.cs y adaptadores FacturaGastoReadAdapter/PagosFacturaCeReadAdapter; revisar enlacesfactura/gasto y control de completitud antes de inferir cero servicios.
- [x] T013 [US2] Extender backend/tests/Api.IntegrationTests/ComercioExterior/CierreServiciosHttpTests.cs para pago/aplicación/reversa porHTTP deTesorería, proyección/evento y relectura; no sustituir pago por NotaCredito en el caso que afirma probar banco.
- [x] T014 [US2] Probar ambos puntos cerrar-financiero/cerrar, incluyendo reversa después del cierrefinanciero y antesGerencia, EXPO, facturaausente/otraempresa/proveedor/sucursal y moneda en backend/tests/ComercioExterior.UnitTests/CierreServiciosCeTests.cs y CierreServiciosHttpTests.cs.
- [x] T015 [US2] Corregir sólo fallas demostradas en backend/src/ComercioExterior/Application/{ExpedienteHandlers,ExpedienteServicio,FinanzasServicio}.cs y puertoslectura; verificar detalle/UI explica proyección sin sugerir otro pago.

## Phase 5: US3 · Entrega y junta
Goal: sólo demostrarlo como listo tras evidenciaactual.
Independent test: acta con actores/documentos/versión y resultado de crédito/anticipo/pedimento.
- [ ] T016 [US3] Ejecutar checksdirigidos y gate completo aplicable de docs/qa/gate-integracion.md; registrarfuera delrepo compilación/modelos, UI, pruebas por caso y fallos anteriores sin ocultaromisiones.
- [ ] T017 [US3] Preparar commit/PR revisable con diff/riesgos/migraciones/evidencia; después de autorización específica y CIverde integrar, conservando contrato unPR/una tarea existente de docs/qa/gate-integracion.md.
- [ ] T018 [US3] Preparar paseQAconcreto desde versionesactuales y respaldo; verificarhistorial/migraciones, API/portal yhealth; no incluir lotes ajenos ni producción. Registrar manifiesto fuera delrepo.
- [ ] T019 [US3] Verificarconfiguración de roles/cuentas para el caso del guion en specs/002-ce-servicios-entrega/quickstart.md; pedir sólo faltantepuntual comprobado y obtener autorización antes de ampliar facultadesreales.
- [ ] T020 [US3] Ejecutar guionQAcrédito y anticipoexcepcional/pedimentoseparado con acta, gasto/pasivo/pago/póliza; conservar N15 y sus6entradas intactas; usar nuevo casoficticioidentificado.
- [ ] T021 [US3] Preparar carpeta dejunta con correoLaura, guion y estadohonesto; actualizar guía local outputs/Guia_Comercio_Exterior_Junta_2026-11-12/ según resultados. No volver a preguntar a/b/c ni presentar como terminada una demo no publicada.

## Phase 6: Polish y cierre integral
- [ ] T022 Evaluar con speckit-converge después de implementar; revisar requisitos de specs/002-ce-servicios-entrega/ sin alterar fuentes.
- [ ] T023 Vincular resultados de Q-CE existente y ensayo de5originales/PAC/OTR en actadeprueba fuera delrepo; registrar aceptación por recorrido separada, sin tareas duplicadas ni nuevos contratosA+W.
- [ ] T024 Actualizar fichasCE/Bitácora y borrador de tareaexterna con fuentes/evidencia; publicar/cerrar sólo con autorización y criterioscumplidos, manteniendo el resto de pendientes clasificados.

## Dependencies
T001→T002→T003/T004/T005→US1→US2→T016→T017→T018→T019→T020→T022/T024. T021 puede prepararse antes con estadohonesto. T023 coordina líneasexistentes y no bloquea correcciónlocalpor faltade datos.

## Parallel opportunities
Después de integrar, T007/T008 y T013/T014 pueden diseñarse en archivos distintos, pero edición de FirmasServiciosHttpTests y de cualquier DbContext/migración se coordina. No se ejecutan gates completos simultáneos en la laptop. Investigación de servicios y comparación fue sólolectura delegada por la skill oficial; no agentes de implementación nuevos.

## Implementation strategy
MVP=US1+US2 probados, porque unservicioa crédito sin pago/cierre no resuelve elobjetivo. Entrega=US3. Reutilizar74archivos ya preparados, sólo reparar huecosdemostrados. No plazosinventados: estimar tras integraciónsemántica, pruebas y accesos de los aprobadores. No hacer aMilletresponsable del código faltante.

## Ejecución en curso
Rama fix/ce-servicios-circuito sobre954a52c7a. Paquete incorporado por tresvías; conflicto de bandeja resuelto preservando helper y extendiendo avisos. Regresión roja de facultadCE reproducida,27dirigidas verdes. HTTP inicial firmas/captura/liberación y cierre:3/3. Sustitución trasN2 reproducida enrojo; guarda y devolución al ciclo de corrección añadidas,37CxPverdes. Ampliación de pagosHTTP/reversa y demásgates todavía pendientes; noCI,merge niQA. Evidencia fuera delrepo.

### 12-nov · circuito HTTP con eventos de producción aprobado
Guardas de cierre y sustitución probadas con creación/línea/transmisión/retransmisión de OC por HTTP, N1, captura real de factura, N2 de otro actor, liberación, gasto CE, pagos40+76 y reversa40. Los consumidores de producción reciben los eventos realmente emitidos, reentregados dos veces: factura registrada entre N1/N2 y pagos/reversa de CxP hacia Compras. OC cierra con total y reabre con reversa; financiero y aprobación final vuelven a rechazar deuda. Nuevo ciclo de sustitución conserva4firmas y una sola factura vigente. Gate_eventos_completos:2/2 aprobadas,16contextos migrados, build0errores/0advertencias. La base física/distribución del ensayo financiero es preparación ficticia explícita; no sustituye el ensayo físico/documental P25 ni QA.

Se reprodujo primero en rojo OC_MOVIMIENTO_ESTADO_INVALIDO al comunicar el pasivo trasN1. Se permite sólo facturación del servicioCE en EnAutorizacionDireccion; pago/recepción conservan sus guardas.975/975Compras aprobadas. El suiteCE previo terminó85/86, única falla en identificador de reversa de la prueba, corregida y comprobada en el ensayo completo; no se presenta esa corrida como verde. Frontend contrato de permisos5/5 tras completar los2 códigos nuevos. Las demás1166UI y832CxP/334API/252CE/472Tesorería conservan su evidencia de esta composición; no se suman como una corrida global nueva. CI remoto y QA aún pendientes, no completarT016/T017/T020.

Al revisar completitud: se controlan las facturas de los gastos identificados en el expediente. Un servicio sin vínculo no puede asignarse por inferencia a otro expediente; la revisión operativa de completitud y el casoQA permanecen explícitos, sin bloquear históricos ni crear un registro universal paralelo. Modelos objetivo reconciliados con snapshots y migraciones aditivas/defaultfalse ensayadas sin asignaciones reales.
