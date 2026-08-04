# PR Breakdown — Módulo Cuentas por Pagar (`Millet.CuentasPorPagar`)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 1.4), [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md), [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 1).
>
> **Estado:** Rev. 2 — consolidado tras feedback de granularidad (`feedback_pr_granularidad.md`).
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `F<fase>-PR<n>` secuencial dentro de la fase.
- **Tamaños**: XS (≤ 200 líneas netas), S (200–500), M (500–800). Techo 800. **Default S-M** (sin XS salvo riesgo aislado).
- **Política de granularidad (feedback Eduardo):** agrupar trabajo afín dentro de la fase. Aislar como PR único solo cuando hay **riesgo real**: migraciones cross-table con datos productivos, transacciones cross-puerto, middleware en hot path, idempotency/outbox/retry, integraciones externas críticas. Cada PR aislado documenta la razón con "**PR aislado por riesgo:** ...".
- **Branch naming**: `cxp/f<fase>-<slug-corto>` (ej. `cxp/f3-factura-con-oc`).
- **Mergeable cuando**: criterio operativo de aceptación.
- **Reuso primero**: cada PR indica qué hereda de Compras / Administración / SharedKernel.

> Convención: PR mergeable = build verde + tests + revisión + cobertura del slice + migraciones con `dotnet ef migrations script` adjunto.

> **Auto-mode N2 activo** para branches `cxp/*` (memoria `feedback_no_commits.md`, extendido 2026-05-22). En estos branches: `git add/commit/push`, `gh pr create/edit/ready/checks` y `gh pr merge --squash --delete-branch` se ejecutan sin pedir permiso explícito. El hook `.claude/hooks/validate-auto-merge.ps1` valida CI verde antes del merge (exit 2 si rojo o pendiente). Cleanup local (branch delete + checkout main + pull) automático tras merge. Notificación al cerrar PR: folio + cambios principales + siguiente PR del breakdown.

---

## Fase 0 — Foundation del módulo (1 PR · S)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F0-PR1 | `cxp/f0-foundation` | **Consolidado**: csproj `Millet.CuentasPorPagar` + folders + smoke endpoint + `CuentasPorPagarDbContext` + schema `cuentas_por_pagar` + migración inicial vacía + tabla `outbox_messages` + `OutboxPublisherWorker<CuentasPorPagarDbContext>` registrado + registro en `MigrationsHealthCheckOptions.ContextTypes` y `deploy-app-dev.yml` + ~25 permisos canónicos `cuentas_por_pagar.*` + puertos cross-module (`IProveedorReadPort`, `IArticuloReadPort`, `ISucursalReadPort`, `IEmpleadoReadPort`, `IPuestoReadPort`, `IDependenciaRevisoraReadPort`, `IConceptoContableReadPort`, `ITipoCambioReadPort`, `IComprasOcReadPort`) con stubs `NoOp*` y `PLATFORM-TODO`. | — | S | medio (touch a `deploy-app-dev.yml`) | `/health/ready` pasa con la nueva BD; smoke endpoint 200/403; los 25 permisos en `identidad.permisos`. |

---

## Fase 1 — `CfdiRecibido` + ingestión manual (1 PR · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F1-PR1 | `cxp/f1-cfdi-ingestion-manual` | **Consolidado**: Agregado `CfdiRecibido` + tabla + estados + VO `UuidCfdi` + parser `XmlCfdiParser` con corpus de tests + comando `IngresarCfdiCommand` + endpoint `POST /cfdis/cargar` (multipart) + dedupe por UUID + almacenamiento Blob (ADR-0024) + bandeja `CfdisPorCapturarQuery` + endpoint `GET /cfdis` paginado + comandos `MarcarCfdiDuplicadoCommand`, `DescartarCfdiCommand` + endpoints respectivos. | F0-PR1 | M | medio (parser robusto + multipart) | Subir XML válido → 201; duplicado por UUID → marcado; bandeja paginada funciona; parser extrae datos de 5+ XMLs de muestra. |

---

## Fase 2 — Ingestión automática (2 PRs · M cada uno)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F2-PR1 | `cxp/f2-fiscalapi-y-descarga-sat` | **Consolidado**: `IFiscalApiClient` + adapter con Polly (retry + circuit breaker) + stub `NoOpFiscalApiClient` con fixtures + Worker `CfdiDescargaMasivaSatWorker` (`IHostedService`) + Worker `CfdiEstadoSatRefreshWorker` (refresca estado cada 6h; detecta cancelaciones en SAT). **PR aislado por riesgo:** integración externa crítica (PAC), workers de fondo, retry/circuit breaker. | F1-PR1 | M | medio | Llamada con cs vacía retorna fixture; workers corren en dev; logs muestran ingestas. |
| F2-PR2 | `cxp/f2-mailbox-ingestion` | Worker `CfdiMailboxIngestionWorker` (Microsoft Graph). Parsea attachments XML+PDF + ingesta vía `ICfdiIngestionPort`. Manejo de attachments malformados. **PR aislado por riesgo:** integración externa (Graph API). | F2-PR1 | M | medio | Mensaje al mailbox con XML adjunto → aparece en bandeja; XML corrupto → archivado en `Failed/` con alerta. |

---

## Fase 3 — `FacturaProveedor` con OC + eventos (2 PRs · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F3-PR1 | `cxp/f3-factura-con-oc` | **Consolidado**: Agregado `FacturaProveedor` + `LineaFacturaProveedor` + tablas + enum `EstadoPasivo` (5) + VOs (`Tolerancia`, `MotivoCancelacion`) + índices §5.1 + CHECK constraints + comando `CapturarFacturaConOcCommand` con match contra OC (vía `IComprasOcReadPort`) y tolerancia del proveedor + endpoints REST (`GET/POST/PATCH/POST cancelar`) + ETag/If-Match + Idempotency-Key. | F0-PR1, F2-PR1 | M | medio | Captura dentro de tolerancia → `Capturada`/`Autorizada`; fuera → `Cancelada` con motivo; CHECK rechaza saldo negativo. |
| F3-PR2 | `cxp/f3-eventos-outbox` | Eventos publicados: `FacturaProveedorRegistradaEvent`, `FacturaProveedorAutorizadaEvent`, `FacturaProveedorRechazadaPorToleranciaEvent`, `FacturaProveedorCanceladaEvent`. Outbox wiring + idempotencia. Si Service Bus cs vacía → log + no-publish. **PR aislado por riesgo:** outbox + integración cross-módulo. | F3-PR1 | S | medio | Outbox publica eventos correctamente; stub loggea si cs vacía. |

---

## Fase 4 — Workflow de revisión + evidencias (2 PRs · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F4-PR1 | `cxp/f4-revision-y-bandeja` | **Consolidado**: Tabla `motivos_revision` con `sla_dias` + seed 14 motivos + `IDependenciaRevisoraReadPort` con stub (A21) + columnas `en_revision`, `motivo_revision_id`, `dependencia_revisora_id` en `facturas_proveedor` + lógica de disparo automático (proveedor en revisión → factura en revisión) + comandos `EnviarFacturaARevisionCommand` + `LiberarRevisionFacturaCommand` + bandeja `FacturasEnRevisionPorAreaQuery` (filtrada server-side). | F3-PR2 | M | medio | Factura entra a revisión por flag del proveedor; responsable de área libera; pasa a `Autorizada`. |
| F4-PR2 | `cxp/f4-evidencias-y-sla` | **Consolidado**: Agregado `EvidenciaAutorizacion` polimórfica + tabla con CHECK polimórfico + upload de adjuntos (Azure Blob) + comando `AdjuntarEvidenciaAutorizacionCommand` + bandeja "Autorizaciones con firma pendiente" + Worker `RevisionSlaNotificacionWorker` (diario 8 AM, día 3/5/10 según motivo SLA — A22) + stub `INotificacionService`. | F4-PR1 | M | medio | Adjuntar evidencia funciona; factura no avanza sin evidencia; worker emite notificaciones (logs si SMTP stub). |

---

## Fase 5 — Integración con Compras y Almacén (1 PR · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F5-PR1 | `cxp/f5-integracion-cross-modulo` | **Consolidado**: Adapter real `ComprasOcReadPort` (reemplaza stub de F0) + tabla `eventos_procesados` para idempotencia (A12 análogo a Almacén) + suscripciones a `OrdenCompraAutorizadaEvent`, `OrdenCompraCanceladaEvent` (Compras) + suscripción a `OcRecepcionRegistradaEvent` (Almacén) con lógica de revisión automática por merma + confirmación de publicación de `FacturaProveedorRegistradaEvent` consumido por Almacén variante B. **PR aislado por riesgo:** múltiples suscripciones cross-módulo + idempotency. | F4-PR2 | M | medio | OCs de Compras se leen; eventos de Compras procesan idempotente; merma de Almacén dispara revisión. |

---

## Fase 6 — NC, anticipos, notas de cargo + ciclo de devolución (3 PRs · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F6-PR1 | `cxp/f6-nota-credito-y-en-espera` | **Consolidado**: Agregado `NotaCreditoProveedor` + tabla + tipos (Descuento/Devolucion/AmortizacionAnticipo) + comando `CapturarNotaCreditoCommand` + vinculación a factura origen por UUID de relación CFDI + estado `EnEspera` para NC pre-factura + Worker `NotaCreditoEnEsperaMatchWorker` (diario 4 AM, A19) + alerta a 30 días sin match + publicación `NotaCreditoProveedorRegistradaEvent`. | F5-PR1 | M | medio | Captura NC tipo 01 reduce saldo; NC sin factura origen → `EnEspera`; worker vincula cuando llega factura. |
| F6-PR2 | `cxp/f6-anticipos-y-notas-cargo` | **Consolidado**: Agregado `AnticipoProveedor` + tabla + serie FANT + comando `CapturarAnticipoCommand` + aplicaciones a facturas (`AplicarAnticipoAFacturaCommand`) + agregado `NotaCargo` + tabla + VO `FolioInternoNotaCargo` con secuencia atómica `NCG-{año}-{secuencial:6}` + comandos `CrearNotaCargoCommand`, `AutorizarNotaCargoCommand`, `AplicarNotaCargoCommand` + autorización Dirección con evidencias (reusa F4-PR2). | F6-PR1 | M | medio | Anticipo aplica a factura, saldo amortizable correcto; folio único bajo concurrencia (100 INSERT paralelos); nota de cargo autoriza y aplica. |
| F6-PR3 | `cxp/f6-ciclo-devolucion-almacen` | **Consolidado**: Suscripción a `OcDevolucionRegistradaEvent` (Almacén sub-flujo 8.B) → crea `NotaCargo` borrador automáticamente + publicación `NotaCreditoFiscalDevolucionRecibidaEvent` cuando llega NC tipo 03 que cierra una devolución (vía Almacén) + correlación bidireccional por `devolucion_a_proveedor_id`. **PR aislado por riesgo:** ciclo bidireccional cross-módulo con correlación crítica. | F6-PR2 | M | medio | Stub de evento de Almacén → NotaCargo borrador creada; NC tipo 03 capturada → evento publicado a Almacén con correlación correcta. |

---

## Fase 7 — Comprobaciones de gastos + TC (6 PRs · M-L)

> **Sub-módulo más complejo del alcance.** El TC se subdivide en 2 PRs por concentración de complejidad (cierre + parser) aislada por riesgo del algoritmo.

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F7-PR1 | `cxp/f7-comprobaciones-base-y-caja-chica` | **Consolidado**: Agregado `ComprobacionGastos` + `LineaComprobacion` + tablas + discriminador `tipo` (4 valores) + estados (5) + adjuntos + variante **Caja Chica** completa (`CrearComprobacionGastosCommand` con tipo `ReembolsoCajaChica`, validación CFDIs a nombre de RFC Millet, cada CFDI genera `FacturaProveedor`, autorización por sucursal). | F6-PR3 | M | medio | Crear comprobación con 5 CFDIs + 2 tickets → genera 5 `FacturaProveedor` ligados; autorización por responsable de sucursal. |
| F7-PR2 | `cxp/f7-aduanales` | Comprobación tipo `GastosAduanales` con OC (Expo vía OTR, Impo OC ad-hoc) + doble autorización (Comercio Exterior + Dirección de Finanzas). | F7-PR1 | M | medio | Aduanales captura OK con doble firma. |
| F7-PR3 | `cxp/f7-catalogos-y-viaticos-electronico` | **Consolidado**: Tablas `aprobadores_limites` y `politicas_viaticos` + stub `IPuestoReadPort` con seed local + CRUD básico para Admin/RH + variante **Viáticos** electrónica completa (solicitud de anticipo con validación contra `politicas_viaticos`, doble autorización jefe + DF si excede, préstamo al empleado `PRESTAMO_EMPLEADO`, comprobación al regresar con liga XML/PDF, saldo a favor/contra empleado, liquidación). | F7-PR1 | L | alto (flujo nuevo end-to-end ~6 pantallas + lógica fiscal de préstamo) | Empleado solicita → jefe aprueba → viaja → comprueba → sistema calcula diferencia → Tesorería paga/cobra. **PR aislado por riesgo:** flujo electrónico nuevo end-to-end. |
| F7-PR4 | `cxp/f7-tc-master-y-movimientos` | **Consolidado**: Master `Tarjeta` + tabla `tarjetas_credito` + tabla `tarjeta_usuarios_autorizados` + CRUD Admin + agregado `MovimientoTarjetaCredito` + tabla + tipos (7) + flujos A (con CFDI) y B (sin CFDI) según §5.1, §5.2 del anexo + Idempotency-Key + multi-moneda con snapshot TC (D9). | F7-PR1 | L | alto (~7 tipos de movimiento, multimoneda, lógica nueva) | Crear tarjeta + 3 usuarios autorizados; movimientos A y B se capturan; gasto contabiliza correctamente; sin CFDI no genera FacturaProveedor. **PR aislado por riesgo:** modelo de TC con multimoneda. |
| F7-PR5 | `cxp/f7-tc-estado-cuenta-y-conciliacion` | **Consolidado**: Agregado `EstadoCuentaTC` + tablas (`estados_cuenta_tc`, `estado_cuenta_tc_lineas_banco`, `estado_cuenta_tc_archivos`) + extensión `pg_trgm` + upload Excel/CSV con dedupe SHA-256 + `IEstadoCuentaTcParserPort` con Strategy por perfil + tabla `perfiles_parser_banco` con seed `AMEX_MX` + algoritmo de match automático con score 0-100 (§7 anexo) + endpoint `POST /tc/estados-cuenta/{id}/conciliar-automatico`. **PR aislado por riesgo:** algoritmo de match + parser configurable (hot path operativo). | F7-PR4 | L | alto (parser robusto + algoritmo de score) | Parser Amex real → N líneas correctas; match ≥90 automático; sugerencias 60-89; sin match <60. |
| F7-PR6 | `cxp/f7-tc-cierre-y-casos-especiales` | **Consolidado**: Comando `CerrarEstadoCuentaTcCommand` + validación (diferencia 0, todos los movimientos atados/explicados) + generación de `FacturaProveedor` agregada contra el banco + evento `EstadoCuentaTcCerradoEvent` + refunds + intereses + comisiones por divisa + anualidades + disputas (§8 anexo) + comando `DisputarMovimientoCommand` + diferencia cambiaria. | F7-PR5 | M | medio | Cerrar estado de cuenta → factura del banco creada → flujo normal de autorización; disputa excluye movimiento; refund vinculado a movimiento original. |

---

## Fase 8 — Reportes (2 PRs · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F8-PR1 | `cxp/f8-reportes-cartera-y-anticipos` | **Consolidado**: Endpoint base con shape JSON estandarizado (ADR-0036, coordinar con Almacén/frontend para `<ReporteShell>`) + `AntiguedadSaldosProveedoresQuery` con buckets de antigüedad y filtros + `AntiguedadAnticiposProveedoresQuery` con columnas separadas (entregado/amortizado/saldo no pagado/pendiente) + `CarteraPorCategoriaRevisionQuery` (cruz subcategoría × revisión × antigüedad). | F7-PR3 | M | medio | 3 reportes funcionales con shape JSON correcto + datos cuadran. |
| F8-PR2 | `cxp/f8-reportes-tc-y-obras` | **Consolidado**: `MovimientosTcPendientesConciliarQuery` + `EstadosCuentaTcConsolidadoQuery` + `PasivosObrasQuery` (expuesto a módulo Obras). | F8-PR1, F7-PR6 | M | bajo | Reportes TC y Obras funcionales. |

---

## Fase 9 — Tesorería wireup (1 PR · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F9-PR1 | `cxp/f9-tesoreria-wireup` | **Consolidado**: Publicación `PasivoAutorizadoParaPagoEvent` cableado a Service Bus real + suscripciones a `PagoFacturaProveedorEvent`, `PagoFacturaProveedorRevertidoEvent`, `ReppProveedorRecibidoEvent`, `CancelacionPasivoSolicitadaEvent` + actualización idempotente de `importe_pagado` y estado de la factura. **PR aislado por riesgo:** integración cross-módulo con sistema externo (Tesorería). | F3-PR2 | M | medio | Tesorería stub publica → CxP actualiza saldo + estado. |

---

## Fase 10 — Hardening y go-live (2 PRs · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F10-PR1 | `cxp/f10-tests-e2e-y-perf` | **Consolidado**: Tests E2E del happy path completo (CFDI → factura → autorizada → pagada) + edge cases (rechazo por tolerancia, anticipo, NC, viáticos, TC) + tests de performance con datos sintéticos (500 facturas/mes, 30 TC/mes, latencia <500ms p95) + audit de `PLATFORM-TODO` restantes con inventario en `08-operacion-y-runbook.md`. | Todas anteriores | M | bajo | E2E pasan en CI; performance reportada; PLATFORM-TODO inventariado. |
| F10-PR2 | `cxp/f10-runbook-y-go-live` | **Consolidado**: `08-operacion-y-runbook.md` (despliegue, observabilidad, troubleshooting, FAQs operativas) + `09-go-live-checklist.md` (catálogos a llenar previo al go-live, validaciones de datos migrados, plan de fallback, comunicación al área). | F10-PR1 | M | bajo | Runbook y checklist revisables por DevOps + firmados por Eduardo + responsable CxP. |

---

## Resumen de granularidad

**Total: 22 PRs** (vs 50 en Rev. 1 — reducción del 56%).

- Fase 0: 1 PR (S)
- Fase 1: 1 PR (M)
- Fase 2: 2 PRs (M) — ambos aislados por riesgo (integración externa)
- Fase 3: 2 PRs (M+S) — PR2 aislado por riesgo (outbox)
- Fase 4: 2 PRs (M)
- Fase 5: 1 PR (M) — aislado por riesgo (cross-módulo)
- Fase 6: 3 PRs (M) — PR3 aislado por riesgo (ciclo bidireccional)
- Fase 7: 6 PRs (M-L) — PR3/PR4/PR5 aislados por riesgo o tamaño
- Fase 8: 2 PRs (M)
- Fase 9: 1 PR (M) — aislado por riesgo (cross-módulo)
- Fase 10: 2 PRs (M)

PRs aislados por riesgo: 8 (F2-PR1, F2-PR2, F3-PR2, F5-PR1, F6-PR3, F7-PR3, F7-PR4, F7-PR5, F9-PR1) — todos documentados con razón explícita.

---

## Rev.

- **2026-05-22 — v2** — Consolidación tras feedback `feedback_pr_granularidad.md`. De ~50 PRs a 22. PRs S-M predominantes. Aislamientos justificados con razón explícita.
- **2026-05-22 — v1** — PR breakdown inicial de 10 fases / ~50 PRs (granularidad excesiva). Sustituido.
