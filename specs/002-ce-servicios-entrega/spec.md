# Feature Specification: Servicios de Comercio Exterior listos para operación

**Feature Branch**: `fix/ce-servicios-circuito`
**Created**: 2026-11-11
**Status**: Implementación local autorizada; validación, integración remota y QA pendientes de evidencia.
**Input**: Eliam pide evaluar si el ERP reproduce las respuestas del hilo de la junta, terminar lo que falta y planear con Spec Kit/Ponytail. Se reutiliza el paquete local COMEX_AF_listo_pruebas; no se solicita nuevamente el proceso a Millet.

## User Scenarios & Testing

### US1 - Un servicio, una deuda, dos firmas (Priority: P1)
CE y CxP necesitan tramitar un servicio a crédito conforme al correo de Laura del11-nov14:53Mérida.
**Why this priority**: El circuito publicado exige autorización completa antes del pasivo y no acredita el orden recibido.
**Independent Test**: Recorrer creación de OC servicio, N1CE, captura de una factura completa, N2Comercial distinta y liberación a Tesorería por interfaces normales.
**Acceptance Scenarios**:
1. Dada OC de servicio, después de N1CE CxP puede registrar el pasivo sin N2; antes de N1 se rechaza.
2. Dado pasivo capturado, N2Comercial de otra persona habilita liberación; sin pasivo, sin facultad o misma persona se rechaza.
3. Dada factura recibida, reintentos o captura por dos rutas no duplican pasivo/gasto; una OC de servicio corresponde a una factura completa.
4. Dada factura discrepante/cancelada/sustituida, su corrección conserva historia y no libera una factura nueva amparándola indebidamente con una revisión anterior; la autorización conserva un objeto verificable.
5. Dada compra ordinaria o documento histórico, conserva su circuito; el indicador CE no se aplica masivamente por inferencia.

### US2 - Cerrar IMPO sólo con servicios conciliados (Priority: P1)
CE necesita comprobar el pago o aplicación válida de sus servicios, sin volver a pagar por una proyección pendiente.
**Why this priority**: El código integrado no consulta pago/saldo de todas las facturas de servicios ligadas a los gastos.
**Independent Test**: Servicio impago→pago parcial→pago total→reversa bancaria; revisar detalle y confirmar cierre por interfaces normales.
**Acceptance Scenarios**:
1. Impago, pago parcial, falta de autorización o factura no verificable bloquean cierre financiero y aprobación final de IMPO con referencia/motivo.
2. Con saldo cero por pago/aplicación válida y demás requisitos completos se permite cerrar; sin cambiar cantidad recibida ni duplicar gasto.
3. Una reversa o proyección bancaria pendiente bloquea y muestra conciliación pendiente; no pide registrar otro pago.
4. EXPO conserva su criterio propio; se respetan empresa, proveedor, sucursal, moneda, versión y permisos.

### US3 - Entrega comprobable y junta útil (Priority: P2)
Eliam necesita distinguir paquete local, código integrado, QA y aceptación, llevando a la junta un caso de operación conocido.
**Why this priority**: Un ensayo de P25 sin el circuito real de servicios no acredita cierre del módulo completo.
**Independent Test**: Guion de crédito habitual y anticipo excepcional con actores/folios y evidencia por paso en versión QA identificada.
**Acceptance Scenarios**:
1. Crédito normal, anticipo excepcional y cargo separado de pedimento conservan sus rutas; el mismo importe no se registra/paga dos veces.
2. CI aplicable y QA tienen evidencia por tramo y versión antes de presentar el servicio como listo; no se amplía el cierre N-15.
3. Decisiones y documentos ya recibidos se reutilizan; sólo se pide precisión nominal/configuración o aceptación específica faltante.

### Edge Cases
Cambio/cancelación/rechazo tras N1 y N2; duplicado concurrente; factura de otra empresa/sucursal/proveedor; deuda en moneda extranjera; notas/créditos/aplicación de anticipo; pago revertido aún no proyectado; históricos sin indicador CE; cuentas inexistentes; servicio no ligado al expediente. Este último debe revisarse como control de completitud, sin inventar facturas que CE no haya identificado.

## Requirements

### Functional Requirements
- **FR-001**: Reflejar ordenCE→N1→pasivoCxP→N2Comercial→Tesorería con personas/facultades correctas.
- **FR-002**: Evitar duplicar factura/gasto/pasivo y evitar una segunda comprobación redundante de firmas.
- **FR-003**: Preservar vigencia de revisión/autorización ante cancelación, sustitución y corrección; cubrir concurrencia/reintentos.
- **FR-004**: Bloquear cierre financiero y aprobación final de IMPO con servicios identificados sin autorización, impagos o no verificables.
- **FR-005**: Contrastar saldoCxP con aplicaciones bancarias vigentes; una reversa/proyección pendiente no simula pago ni provoca otro pago.
- **FR-006**: Conservar compras ordinarias, documentos históricos, EXPO, aislamiento y controles existentes.
- **FR-007**: Reutilizar crédito habitual, anticipo excepcional y pago separado del pedimento; gasto actual a cuenta de gasto, sin aumentar inventario.
- **FR-008**: Reutilizar correcciones locales, reconciliar develop actual, preparar migraciones/configuración reversibles y preservar otros lotesQA.
- **FR-009**: Exigir evidencia de flujo completo por interfaces, CI, QA y conformidad separadas; no reabrir ni extender N-15 por inferencia.
- **FR-010**: Registrar quién atiende cada pendiente y su resultado observable; no repetir solicitudes recibidas ni convertir datos pendientes en bloqueo general.

### Key Entities
OC de servicio e indicador CE; autorizaciónN1/N2; factura/pasivo y su versión; gasto del expediente; pago/aplicación/reversa; cierres; evidencia de prueba. Reutilizar identidades actuales.

## Success Criteria
- **SC-001**: Un caso de crédito recorre N1→captura→N2→liberación sin inserciones que sustituyan acciones del flujo, con exactamente una factura y deuda.
- **SC-002**: Todos los casos de permiso, discrepancia, duplicado y sustitución definidos pasan sin persistir cambios no autorizados.
- **SC-003**: Impago/parcial/reversa/proyección pendiente bloquean ambos puntos de cierre; total válido libera, conservando resto de requisitos.
- **SC-004**: Compras ordinarias, EXPO y documentos históricos mantienen resultados anteriores en regresiones pertinentes.
- **SC-005**: QA muestra versión, actores, folios, pagos/aplicaciones, póliza y resultado por paso; aceptación Millet queda explícita y separada.

## Assumptions and boundaries
Regla recibida: correo Laura1a1cc635fed37396. Original23oct/recibido26oct confirma una OC por factura. No hacen falta nuevas preguntas generales. No se asignan roles ni cuentas reales en este trabajo. Fuera: AF, rehacer P25, emisor aduanal, retornoERP→OTR, anticiposuniversales, producción. El cierre integral CE también exige Q-CE/PAC, originales y aceptación, registrados en plan. Eliam autorizó ejecutar el plan, reutilizar el paquete, integrar con develop y probar el flujo antes del pase QA. Se mantienen los gates y la separación entre código integrado, QA y aceptación.
