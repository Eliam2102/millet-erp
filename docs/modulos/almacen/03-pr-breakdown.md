# PR Breakdown — Módulo Almacén (`Millet.Almacen`)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 1), [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 1).
>
> **Estado:** Rev. 2 — consolidado tras feedback de granularidad (`feedback_pr_granularidad.md`).
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `F<fase>-PR<n>`.
- **Tamaños**: XS (≤ 200), S (200–500), M (500–800). Techo 800. **Default S-M** (sin XS salvo riesgo aislado).
- **Política de granularidad (feedback Eduardo):** agrupar trabajo afín dentro de la fase. Aislar PR único solo cuando hay **riesgo real**: migraciones cross-table con datos productivos, transacciones cross-puerto, middleware en hot path, idempotency/outbox, integraciones externas críticas, lógica de seguridad. Cada PR aislado documenta "**PR aislado por riesgo:** ...".
- **Branch naming**: `almacen/f<fase>-<slug-corto>`.
- Convención: PR mergeable = build verde + tests + revisión + migración con `dotnet ef migrations script` adjunto.

> **Auto-mode N2 activo** para branches `almacen/*` (memoria `feedback_no_commits.md`, extendido 2026-05-22). En estos branches: `git add/commit/push`, `gh pr create/edit/ready/checks` y `gh pr merge --squash --delete-branch` se ejecutan sin pedir permiso explícito. El hook `.claude/hooks/validate-auto-merge.ps1` valida CI verde antes del merge (exit 2 si rojo o pendiente). Cleanup local automático tras merge. Notificación al cerrar PR: folio + cambios principales + siguiente PR del breakdown.

---

## Fase 0 — Foundation (1 PR · S)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F0-PR1 | `almacen/f0-foundation` | **Consolidado**: csproj `Millet.Almacen` + folders + smoke endpoint + `AlmacenDbContext` + schema `almacen` + migración inicial vacía + `outbox_messages` + `OutboxPublisherWorker<AlmacenDbContext>` + registro en `MigrationsHealthCheckOptions.ContextTypes` y `deploy-app-dev.yml` + ~30 permisos canónicos `almacen.*` + puertos cross-module (`IComprasOcReadPort`, `IComprasRequisicionReadPort`, `IArticuloReadPort`, `IProveedorReadPort`, `ISucursalReadPort`, `IEmpleadoReadPort`, `ITipoCambioReadPort`, `IConceptoContableReadPort`, `IPeriodoContableReadPort`) con stubs `NoOp*` y `PLATFORM-TODO`. | — | S | medio | `/health/ready` pasa; smoke 200/403; permisos en `identidad.permisos`. |

---

## Fase 1 — Re-localización catálogo Almacén/SubAlmacén (3 PRs · M+S+S)

> **Coordinación con módulo Administración** crítica. Los 3 PRs van secuenciales con verificación entre ellos.

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F1-PR1 | `almacen/f1-catalogo-y-endpoints` | **Consolidado**: Agregado `Almacen` + `SubAlmacen` + tablas `almacen.almacenes` y `almacen.sub_almacenes` + **copy-from** `compartido.almacenes` en migración (aditiva, sin DROP) + endpoints CRUD + re-apuntar `IAlmacenReadPort` consumido por CxP/Compras al nuevo schema. **PR aislado por riesgo:** touch a tabla compartida (compartido.almacenes) con copy de datos productivos. | F0-PR1 | M | **alto** | Migración aplica; ambas tablas con datos consistentes; CxP/Compras leen del nuevo schema; smoke tests cross-módulo pasan. |
| F1-PR2 | `almacen/f1-drop-placeholder` | **DROP** de tablas del placeholder en `compartido.*`. Pre-validación: `rg "compartido\.almacenes" backend/src/` = 0 hits. **PR aislado por riesgo:** DROP irreversible de tabla compartida. | F1-PR1 | S | **alto** | Tablas eliminadas; CI verde en todos los módulos; no hay regressions en smoke tests. |
| F1-PR3 | `almacen/f1-seed-inicial-sap` | Script de seed con almacenes/sub-almacenes reales desde SAP + sub-almacén especial `MATERIAL_EN_REVISION` por sucursal (A15). | F1-PR2 | S | medio | Catálogo poblado en dev con datos de SAP. |

---

## Fase 2 — `MovimientoInventario` base + saldos + Recepción Variante A (2 PRs · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F2-PR1 | `almacen/f2-movimientos-y-saldos` | **Consolidado**: Agregado `MovimientoInventario` + `LineaMovimiento` + tablas (polimórfica con discriminador) + estados (4) + VO `FolioMovimiento` + secuencias `folio_secuencias` por tipo + índices §5.2 + CHECK constraints + tabla `saldos_inventario` (materializada) + trigger BEFORE INSERT (estado `Registrado`) que actualiza saldo en la misma transacción + CHECK `cantidad >= 0`. **PR aislado por riesgo:** trigger PG + lógica transaccional de saldo (la pieza más crítica del módulo). | F1-PR3 | M | **alto** | 100 inserts en sucesión → saldo correcto; rollback parcial preserva consistencia; CHECKs validan. |
| F2-PR2 | `almacen/f2-recepcion-variante-a` | **Consolidado**: Comando `RegistrarRecepcionConFacturaCommand` (Variante A — insumos) + validación contra OC vía `IComprasOcReadPort` + tolerancia por material (A5) + costo de OC + endpoints `POST/GET /recepciones` + bandeja paginada + Idempotency-Key + ETag + evento `OcRecepcionRegistradaEvent` publicado al outbox + query `SaldoInventarioPorSubAlmacenQuery` + endpoint `GET /saldos` + **`IAlmacenSaldoQueryPort` público en `Almacen.Domain.Ports.Public`** (Open Host Service para Compras) + adapter en `Almacen.Infrastructure.PublicAdapters` que consulta `saldos_inventario` read-only. | F2-PR1 | M | medio | curl POST → 201 con folio; saldo actualiza; outbox tiene evento; bandeja muestra recepción; el puerto `IAlmacenSaldoQueryPort` devuelve saldo correcto consultado desde otro test (simula uso desde Compras). |

---

## Fase 3 — Recepción Variante B + integración CxP (1 PR · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F3-PR1 | `almacen/f3-variante-b-y-cxp` | **Consolidado**: Comando `RegistrarRecepcionConPackingListCommand` (Variante B) con flag `factura_pendiente=true` + costo de OC + adjunto packing list + tabla `eventos_procesados` para idempotencia (A12) + wrapper genérico de listener con dedupe + suscripción a `FacturaProveedorRegistradaEvent` (CxP) con match por OC + suscripción a `DiferenciaPrecioFacturaDetectadaEvent` (CxP) con ajuste de costo del inventario remanente proporcional (A11) + movimiento `AjustePrecioFactura` + publicación `EntradaInventarioValoradaEvent` recalculado. **PR aislado por riesgo:** múltiples suscripciones cross-módulo + idempotencia + ajuste de costo. | F2-PR2 | M | medio | Recepción B con `factura_pendiente=true`; stub de evento CxP dispara conciliación; diferencia de precio ajusta costo correctamente; mismo evento dos veces → un solo efecto. |
| F3-PR2 | `almacen/f3-reservas-stock` | **Consolidado** (A19, decisión 2026-05-22 opción B del teardown): Agregado `ReservaStock` + tabla `reservas_stock` con CHECKs + columna `cantidad_reservada` en `saldos_inventario` con CHECK `cantidad_reservada <= cantidad` + columna generada `cantidad_disponible` + comandos `ReservarStockCommand` y `LiberarReservaCommand` + endpoints `POST /reservas` y `POST /reservas/{id}/liberar` + **puerto público `IAlmacenReservaPort`** en `Almacen.Domain.Ports.Public` + adapter real en `Almacen.Infrastructure.PublicAdapters` + eventos `StockReservadoEvent` y `StockLiberadoEvent` publicados. **Reemplaza** `InMemoryReservarStockPort` y `InMemoryLiberarReservaPort` de Compras (ver `_teardown-stubs-almacen-cxp.md`). **PR aislado por riesgo:** lógica transaccional concurrente sobre `saldos_inventario` (reservar requiere `SELECT FOR UPDATE` + validación de `cantidad_disponible`). | F2-PR2 | M | alto | Reservar 30 contra stock 100 → `cantidad_reservada=30`, `cantidad_disponible=70`. Reservar otro 80 → falla (excede disponible). Liberar reserva → `cantidad_reservada=0`. Tests concurrentes: 2 reservas paralelas a stock 100 — solo una gana, la otra falla. |

---

## Fase 4 — Salida normal con RQ + consumo de reservas (1 PR · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F4-PR1 | `almacen/f4-salida-normal` | **Consolidado**: `RegistrarSalidaCommand` (Variante A) con validación de stock (`SELECT ... FOR UPDATE`) + RQ aprobada (vía `IComprasRequisicionReadPort`) + **lookup de reserva activa** por `(documento_origen_tipo='Requisicion', documento_origen_id=rq_id)`; si existe → consume reserva (estado `Consumida`, decrementa `cantidad` y `cantidad_reservada`); si NO existe → consumo directo (decrementa solo `cantidad`) + costo promedio ponderado actual con snapshot en línea de salida (A9) + endpoints `POST/GET /salidas` + bandeja + evento `SalidaRequisicionRegistradaEvent` + comprobante PDF (stub si servicio PDF no disponible) + suscripción a `RequisicionAprobadaEvent`/`RequisicionCanceladaEvent` (Compras) + reporte operativo "Salidas del día" (PDF + Excel). | F3-PR2 | M | medio | Salida se registra; saldo decrementa; salidas concurrentes no dejan stock negativo; RQ con reserva activa → consume reserva al surtir; RQ sin reserva → consumo directo. |

---

## Fase 5 — Vale + Devoluciones internas (1 PR · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F5-PR1 | `almacen/f5-vale-y-devolucion-interna` | **Consolidado**: `RegistrarSalidaPorValeCommand` con flag `pendiente_regularizacion=true` y `fecha_limite=+48h` (A14) + adjunto del vale firmado + `RegularizarSalidaPorValeCommand` que vincula RQ posterior + Worker `RegularizacionValeSlaWorker` (diario 9 AM, notifica día 1/2 — A14) + agregado `DevolucionInterna` (sub-flujo 8.A) + comandos `IniciarDevolucionInternaCommand` y `AplicarDevolucionInternaCommand` + restitución al sub-almacén origen al costo de la salida original (A9) + evento `DevolucionInternaAplicadaEvent` + comandos `BajaPorDano` y `ReincorporacionTrasRevision` para sub-almacén `MATERIAL_EN_REVISION` (A15). | F4-PR1 | M | medio | Vale se registra; regularización funciona; devolución interna restituye al costo correcto; material dañado se mueve a `MATERIAL_EN_REVISION`. |

---

## Fase 6 — Devolución a Proveedor (sub-flujo 8.B) (1 PR · L)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F6-PR1 | `almacen/f6-devolucion-proveedor` | **Consolidado**: Agregado `DevolucionAProveedor` (extiende `MovimientoInventario`) + tabla + comando `IniciarDevolucionAProveedorCommand` + `SolicitarAutorizacionDevolucionAProveedorCommand` con evidencia (mismo patrón que `NotaCargo` en CxP) + `RegistrarSalidaDevolucionAProveedorCommand` con costo al de recepción original (A10) + evento `OcDevolucionRegistradaEvent` publicado + suscripción a `NotaCreditoFiscalDevolucionRecibidaEvent` (CxP) que marca devolución como `ConciliadaConNcFiscal` + bandeja "Devoluciones pendientes de NC fiscal" + dashboard de conciliación. **PR aislado por riesgo:** ciclo bidireccional con CxP, correlación crítica, autorización de Dirección. | F5-PR1 | L | alto (~5 endpoints + ciclo bidireccional) | Iniciar devolución → autorización Dirección → registrar salida → evento publicado a CxP → NC fiscal recibida → conciliada. |

---

## Fase 7 — Inventario físico (3 PRs · M)

> Los 3 PRs aislados por riesgo de la lógica del módulo: snapshot inmutable, captura sin sesgo (lógica de seguridad UX), bloqueo de salidas.

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F7-PR1 | `almacen/f7-conteo-y-captura-sin-sesgo` | **Consolidado**: Agregados `ConteoInventario` + `LineaConteo` + `RecuentoConteo` + tablas + estados (5) + comandos `CrearConteoInventarioCommand` + `IniciarConteoCommand` (snapshot teórico en transacción al iniciar) + endpoint `POST /conteos/{id}/lineas/{linea_id}/capturar` que **NO** envía `cantidad_teorica` al contador (A6) — solo recibe `cantidad_real` + endpoint separado `GET /conteos/{id}/lineas/{linea_id}/comparacion` para aprobador. **PR aislado por riesgo:** lógica de seguridad UX (captura sin sesgo) — un solo endpoint mal expuesto rompe la mejora de proceso. | F6-PR1 | M | alto | Endpoint del contador NUNCA devuelve `cantidad_teorica` (test con permiso de contador); endpoint del aprobador sí; snapshot frozen durante el conteo. |
| F7-PR2 | `almacen/f7-recuento-y-aprobacion` | **Consolidado**: Lógica de recuento obligatorio cuando variación > umbral (A7) + comando `AgregarRecuentoCommand` + pantalla de aprobación con bulk approve + política por monto (A8: <$1K Almacenista, 1K-10K Supervisor, >10K Jefe + Finanzas) + comandos `EnviarAprobacionConteoCommand`, `AprobarConteoCommand`, `AplicarConteoCommand` que genera movimientos `AjustePositivo`/`AjusteNegativo` en batch transaccional + evento `AjusteInventarioAplicadoEvent`. | F7-PR1 | M | medio | Variación > umbral marca línea; aprobación respeta política; aplicar genera N movimientos; saldos cuadran. |
| F7-PR3 | `almacen/f7-bloqueo-inventario-anual` | Tabla `bloqueos_inventario` + validación en `RegistrarSalidaCommand` consulta bloqueos activos + bloqueo automático al pasar conteo anual a `EnCurso` + libera al `Aplicado` (A18). **PR aislado por riesgo:** cross-cutting en hot path de salidas. | F7-PR2 | S | alto | Salida durante anual rechazada con mensaje claro; entrada sí permitida. |

---

## Fase 8 — Reportes y cierre de mes (2 PRs · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F8-PR1 | `almacen/f8-reportes-alfak-y-mp-cnk` | **Consolidado**: Endpoint base con shape JSON estandarizado (ADR-0036, coordinar con CxP F8) + reporte **ALFAK-HISTORIAL-ALMACEN** (cierre de mes, agrupa por sub-almacén/articulo, calcula entradas/salidas/ajustes/saldo final) en PDF + Excel + reporte **SAP-REPORTE-EXISTENCIA-MP-CNK** (inventario diario MP) en PDF + Excel. | F7-PR3 | M | medio | Reporte cuadra con saldos al cierre del mes; MP-CNK muestra inventario actual de MP correctamente. |
| F8-PR2 | `almacen/f8-cierre-mes-y-periodo-cerrado` | **Consolidado**: Comando `EjecutarCierreMensualCommand` con validaciones (todos los movimientos registrados, no hay conteos pendientes) + tabla `periodos_cerrados` + validación en TODOS los comandos: `fecha_movimiento` en periodo cerrado → 422 con detalle. **PR aislado por riesgo:** validación cross-cutting en todos los comandos de movimiento. | F8-PR1 | M | alto | Cerrar mes con conteo pendiente rechaza; cerrar OK marca periodo; movimiento con fecha de mes cerrado → 422. |

---

## Fase 9 — Hardening y go-live (1 PR · M)

| ID | Título | Alcance | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|
| F9-PR1 | `almacen/f9-hardening-runbook-go-live` | **Consolidado**: Tests E2E del ciclo completo (recepción → salida → conteo → cierre → reporte) + performance tests (5K mov/mes sintéticos, latencia bandejas <500ms p95) + audit y cierre de `PLATFORM-TODO` con inventario + `08-operacion-y-runbook.md` (despliegue, observabilidad, troubleshooting, FAQs) + `09-go-live-checklist.md` (validación de catálogos migrados de SAP, plan de fallback, comunicación con Carlos). | Todas anteriores | M | bajo | E2E pasan; performance documentada; runbook revisable por DevOps; checklist firmada por Eduardo + Carlos. |

---

## Resumen de granularidad

**Total: 14 PRs**.

- Fase 0: 1 PR (S)
- Fase 1: 3 PRs (M+S+S) — los 3 aislados por riesgo (touch a `compartido.*` con copy + DROP irreversible)
- Fase 2: 2 PRs (M) — PR1 aislado por riesgo (trigger PG + transacción)
- Fase 3: 2 PRs (M+M) — PR1 aislado (cross-módulo + idempotencia + ajuste de costo), PR2 aislado (reservas con lógica transaccional concurrente A19)
- Fase 4: 1 PR (M)
- Fase 5: 1 PR (M)
- Fase 6: 1 PR (L) — aislado por riesgo (ciclo bidireccional con CxP)
- Fase 7: 3 PRs (M+M+S) — todos aislados por riesgo
- Fase 8: 2 PRs (M) — PR2 aislado por riesgo
- Fase 9: 1 PR (M)

PRs aislados por riesgo: 10 (F1-PR1, F1-PR2, F2-PR1, F3-PR1, F3-PR2, F6-PR1, F7-PR1, F7-PR3, F8-PR2). Cada uno con razón explícita.

---

## Rev.

- **2026-05-22 — v3** — Agregado F3-PR2 (Reservas de stock — A19, opción B del teardown de Compras). Total: 14 PRs.
- **2026-05-22 — v2** — Consolidación tras feedback `feedback_pr_granularidad.md`. De ~45 PRs a 13.
- **2026-05-22 — v1** — PR breakdown inicial de 10 fases / ~45 PRs (granularidad excesiva). Sustituido.
