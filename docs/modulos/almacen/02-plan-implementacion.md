# Plan de implementación — Módulo Almacén (`Millet.Almacen`)

> **Construido sobre:**
> - [00-levantamiento.md](00-levantamiento.md) (Rev. 0.1.1) y
> - [01-diseno.md](01-diseno.md) (Rev. 1).
>
> **Estado:** propuesta de plan para revisión con el equipo. Sizing en bandas — calibrar contra capacidad real.
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Sizing: **XS** 1–2d · **S** 3–5d · **M** 1–2sem · **L** 3–4sem · **XL** >1mes.
- Cada fase produce algo **deployable y validable**.
- Fases secuenciales por dependencia técnica; paralelismo interno marcado "‖".
- **Reuso primero**: indicar qué hereda antes de listar lo nuevo.

---

## 1. Resumen ejecutivo

**Objetivo:** entregar v1 del módulo `Millet.Almacen`, alineado con `01-diseno.md` Rev. 1.

**Estrategia:**

1. **Re-localizar el placeholder `Almacen` de `DatosMaestros`** al módulo nuevo como primer paso (F1-PR1). Migración aditiva: tablas nuevas en `almacen.*`, copy-from `compartido.*`, drop del placeholder al final de F1.
2. **Walking skeleton primero**: crear un sub-almacén, capturar una recepción simple, ver el saldo. Solo después agregar Vale, Devolución, Inventario físico.
3. **Reuso máximo** del módulo Compras: `Outbox`, `BaseEntity`, `RBAC`, `FluentValidation`, `ProblemDetails`, `Idempotency`, `ETag`, `SoftLock`, `FolioSecuencia`, `IntegrationEventPublisher`.
4. **Saldo materializado** (`saldos_inventario`) actualizado en la misma transacción que el movimiento. No hay job de "recalcular saldos" — la consistencia es transaccional.
5. **Stubs cross-module** para CxP, Contabilidad, Finanzas (calendar fiscal). Patrón ADR-0031.
6. **Coordinación con CxP** en Fases 2-3-6 (los eventos cruzados).
7. **Inventario físico se aborda al final** (F7) porque es el más complejo y no bloquea el ciclo operativo diario.

**Pendientes que NO bloquean arranque:**

- Operación móvil con escáner — vNext.
- Bins/ubicaciones formales — vNext.
- Conteo cíclico por ABC — vNext (manual en MVP).
- Traspasos internos — vNext (a confirmar con Carlos).

---

## 2. Prerrequisitos — audit del repo (2026-05-22)

| Prerrequisito | Estado | Detalle / plan B |
|---|---|---|
| Proyecto `Millet.Almacen` | ❌ no existe | F0-PR1 lo crea. |
| `AlmacenDbContext` + schema `almacen` | ❌ no existe | F0-PR2 lo crea + registra en `MigrationsHealthCheckOptions.ContextTypes` y en `deploy-app-dev.yml`. |
| Placeholder `Almacen` en `DatosMaestros` | ⚠️ existe (MVP-light) | Re-localizar en F1. Coordinar con módulo Administración antes de mergear (migración aditiva no breaking). |
| `BaseEntity`, `BaseDbContext`, interceptors | ✅ existe | Reusable. |
| EF Core + migraciones por esquema | ✅ existe | Patrón establecido. |
| Identidad + RBAC + Entra ID | ✅ existe | Permisos `almacen.*` en F0-PR3. |
| Problem Details | ✅ existe | Estándar. |
| `Money`, `Moneda` VOs | ✅ existe | Reusable. |
| Soft delete | ✅ existe | Reusable. |
| Health checks | ✅ existe | Cubre migraciones automáticamente. |
| `CollaborationHub` SignalR | ✅ existe | `MovimientoInventario` se declara con awareness. |
| Patrón Outbox | ✅ existe | `OutboxPublisherWorker<AlmacenDbContext>` en F0-PR2. |
| Patrón Folio + secuencia atómica | ✅ existe | `almacen.folio_secuencias` siguiendo el patrón. |
| `IComprasOcReadPort` / `IComprasRequisicionReadPort` | ⏳ a confirmar | Si no existen, los define este módulo en F0-PR4 con stubs y los pasa al equipo de Compras. |
| `IArticuloReadPort` / `ISucursalReadPort` / `IEmpleadoReadPort` | ⏳ a confirmar con DatosMaestros | Stubs locales o adapters reales según disponibilidad. |
| CxP — eventos (`FacturaProveedorRegistradaEvent`, etc.) | ⏳ paralelo | CxP en construcción en paralelo. Stubs `NoOpCxpEventConsumer` hasta runtime. |
| OpenAPI + tipos TS | ✅ existe | Pipeline automático. |

---

## 3. Estrategia de paralelización con CxP

Compartimos 5 eventos críticos (§7 del 01-diseno). Coordinación:

- **Convención de naming**: `{Agregado}{Verbo}Event` (canon de Compras-OC §8.5).
- Almacén publica: `OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent`.
- Almacén suscribe: `FacturaProveedorRegistradaEvent`, `DiferenciaPrecioFacturaDetectadaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`.
- Sesión semanal con el equipo de CxP durante F2-F6.
- Stubs `NoOpCxpEventConsumer` y `NoOpCxpEventPublisher` mientras CxP no esté en runtime.

---

## 4. Fases

### Fase 0 — Foundation del módulo (S)

- F0-PR1: csproj `Millet.Almacen` + folders. Smoke endpoint.
- F0-PR2: `AlmacenDbContext` + schema `almacen` + migración vacía. `OutboxPublisherWorker<AlmacenDbContext>` registrado.
- F0-PR3: Permisos canónicos `almacen.*` (~30 permisos del §10 del 01-diseno).
- F0-PR4: Puertos de lectura cross-module (`IComprasOcReadPort`, `IComprasRequisicionReadPort`, `IArticuloReadPort`, etc.) con stubs o adapters según disponibilidad.

**Paralelización:** F0-PR1 → F0-PR2 → (F0-PR3 ‖ F0-PR4).

### Fase 1 — Catálogo `Almacen` + `SubAlmacen` y re-localización (M)

- F1-PR1: Agregado `Almacen` + `SubAlmacen` + tablas. Coordinación con módulo Administración: copy-from `compartido.almacenes` a `almacen.almacenes`. Tabla nueva vive en paralelo con la antigua.
- F1-PR2: Endpoints CRUD de `Almacen` y `SubAlmacen`. Re-apuntar `IAlmacenReadPort` que consume CxP/Compras al nuevo schema.
- F1-PR3: DROP de tablas del placeholder en `DatosMaestros` (después de validar que ningún módulo lee desde ahí). PR coordinado con Administración.
- F1-PR4: Seed inicial de almacenes y sub-almacenes desde SAP (script).

**Paralelización:** secuencial F1-PR1 → F1-PR2 → F1-PR3. F1-PR4 paralelo a F1-PR2.

> **Riesgo crítico:** F1-PR3 (DROP) requiere doble verificación. Si algún módulo consulta directo a `compartido.almacenes`, se queda en deuda hasta migrarse. Esto se documenta en `04-cuidados-infra.md` §1.2.

### Fase 2 — `MovimientoInventario` base + Recepción Variante A (M)

Walking skeleton: capturar una entrada con factura, ver el saldo.

- F2-PR1: Agregado `MovimientoInventario` + `LineaMovimiento` + tablas. Discriminador por `tipo`. Estados (4).
- F2-PR2: Tabla `saldos_inventario` (materializada). Trigger BEFORE INSERT del movimiento `Registrado` que actualiza saldo en la misma transacción.
- F2-PR3: Comando `RegistrarRecepcionConFacturaCommand` (Variante A — insumos). Validación contra OC (vía `IComprasOcReadPort`).
- F2-PR4: Endpoint `POST /recepciones` + bandeja `GET /recepciones`. Evento `OcRecepcionRegistradaEvent` publicado.
- F2-PR5: `SaldoInventarioPorSubAlmacenQuery` + endpoint `GET /saldos`. Validación de que el saldo cuadra después de N movimientos.

**Paralelización:** secuencial.

### Fase 3 — Recepción Variante B (packing list) + integración CxP (M)

- F3-PR1: Comando `RegistrarRecepcionConPackingListCommand` (Variante B — materiales directos). Flag `factura_pendiente = true`.
- F3-PR2: Suscripción a `FacturaProveedorRegistradaEvent` (CxP). Match contra recepción pendiente; marca `ConciliadaConFactura`.
- F3-PR3: Suscripción a `DiferenciaPrecioFacturaDetectadaEvent` (CxP). Genera movimiento `AjustePrecioFactura` que ajusta costo del inventario remanente (A11). Mismo handler dispara `EntradaInventarioValoradaEvent` recalculado.
- F3-PR4: Tabla `eventos_procesados` para idempotencia (A12). Listener wrapper que verifica antes de procesar.

**Paralelización:** F3-PR1 → F3-PR2 → F3-PR3 → F3-PR4.

### Fase 4 — Salida normal con RQ (M)

- F4-PR1: Comando `RegistrarSalidaCommand` (Variante A — normal). Validación de stock + RQ aprobada (vía `IComprasRequisicionReadPort`). Comprobante PDF.
- F4-PR2: Endpoint `POST /salidas` + bandeja. Evento `SalidaRequisicionRegistradaEvent`.
- F4-PR3: Suscripción a `RequisicionAprobadaEvent` / `RequisicionCanceladaEvent` (Compras). Proyección local opcional.
- F4-PR4: Bandeja "Salidas del día" + reporte interno PDF (recurso operativo del Almacenista).

**Paralelización:** F4-PR1 → F4-PR2; F4-PR3 ‖ F4-PR4.

### Fase 5 — Salida por Vale + Devoluciones internas (M)

- F5-PR1: Comando `RegistrarSalidaPorValeCommand` (Variante B). Flag `pendiente_regularizacion = true` con `fecha_limite = +48h`. Adjunto del vale firmado.
- F5-PR2: Comando `RegularizarSalidaPorValeCommand`. Vincula RQ posterior.
- F5-PR3: Worker `RegularizacionValeSlaWorker` (notifica día 1 y 2 si no se regulariza — A14).
- F5-PR4: Agregado `DevolucionInterna` (sub-flujo 8.A). Comando + endpoint. Restitución al sub-almacén origen al costo de la salida original (A9). Evento `DevolucionInternaAplicadaEvent`.
- F5-PR5: Sub-almacén especial `MATERIAL_EN_REVISION` por sucursal (A15). Comandos `BajaPorDano`, `ReincorporacionTrasRevision`.

**Paralelización:** F5-PR1 → F5-PR2 → F5-PR3; F5-PR4 ‖ F5-PR5.

### Fase 6 — Devolución a Proveedor (sub-flujo 8.B) (M)

Cierra el ciclo bidireccional con CxP.

- F6-PR1: Agregado `DevolucionAProveedor` (extiende `MovimientoInventario` tipo `SalidaPorDevolucionAProveedor`). Comando `IniciarDevolucionAProveedorCommand`.
- F6-PR2: Autorización de Dirección con evidencia (compartida con CxP §8.2 del 00-levantamiento de CxP). Comando `SolicitarAutorizacionDevolucionAProveedorCommand`.
- F6-PR3: Comando `RegistrarSalidaDevolucionAProveedorCommand`. Costo al de recepción original (A10). Evento `OcDevolucionRegistradaEvent` publicado.
- F6-PR4: Suscripción a `NotaCreditoFiscalDevolucionRecibidaEvent` (CxP). Marca devolución como `ConciliadaConNcFiscal`.
- F6-PR5: Bandeja "Devoluciones a proveedor pendientes" + dashboard de conciliación con NC fiscales.

**Paralelización:** F6-PR1 → F6-PR2 → F6-PR3 → F6-PR4 → F6-PR5.

### Fase 7 — Inventario físico (L)

El más complejo. Conteo rotativo + anual + recuento + aprobación por monto.

- F7-PR1: Agregado `ConteoInventario` + `LineaConteo` + `RecuentoConteo` + tablas.
- F7-PR2: Comando `CrearConteoInventarioCommand` + `IniciarConteoCommand` (toma snapshot en transacción).
- F7-PR3: Pantalla "Captura sin sesgo" — endpoint que **no** envía cantidad teórica al contador (A6). Comando `CapturarLineaConteoCommand`.
- F7-PR4: Lógica de recuento obligatorio cuando variación > umbral (A7). Comando `AgregarRecuentoCommand`.
- F7-PR5: Pantalla de aprobación por monto (A8). Comandos `EnviarAprobacionConteoCommand`, `AprobarConteoCommand`.
- F7-PR6: Comando `AplicarConteoCommand` — genera movimientos `AjustePositivo` / `AjusteNegativo` en batch transaccional. Evento `AjusteInventarioAplicadoEvent`.
- F7-PR7: Bloqueo de salidas durante conteo anual (A18). Tabla `bloqueos_inventario` + validación en `RegistrarSalidaCommand`.

**Paralelización:** secuencial salvo F7-PR4 y F7-PR5 que pueden paralelizarse después de F7-PR3.

### Fase 8 — Reportes y cierre de mes (M)

- F8-PR1: `<ReporteShell>` compartido si no existe (coordinación con CxP F8).
- F8-PR2: Reporte **ALFAK-HISTORIAL-ALMACEN** (cierre de mes). Endpoint + componente + PDF + Excel.
- F8-PR3: Reporte **SAP-REPORTE-EXISTENCIA-MP-CNK** (inventario diario MP).
- F8-PR4: Comando `EjecutarCierreMensualCommand`. Validaciones: todos los movimientos del mes registrados, no hay conteos `EnConciliacion`/`Aprobado` sin aplicar.
- F8-PR5: Tabla `periodos_cerrados` + validación en cualquier comando con `fecha_movimiento` en periodo cerrado.

**Paralelización:** F8-PR1 obligatorio. F8-PR2 ‖ F8-PR3 ‖ F8-PR4 ‖ F8-PR5.

### Fase 9 — Hardening y go-live (S)

- F9-PR1: Tests E2E del ciclo completo (recepción → salida → cierre).
- F9-PR2: Performance tests (5K movimientos/mes simulados).
- F9-PR3: Auditoría de PLATFORM-TODO restantes.
- F9-PR4: `08-operacion-y-runbook.md` (despliegue, observabilidad, soporte L1/L2).
- F9-PR5: `09-go-live-checklist.md`.

---

## 5. Cronograma sugerido (con un equipo de 2 devs backend + 1 dev frontend)

| Mes | Fases |
|---|---|
| 1 | F0 + F1 + F2 |
| 2 | F3 + F4 |
| 3 | F5 + F6 |
| 4 | F7 (Inventario físico) |
| 5 | F8 (Reportes + cierre de mes) |
| 6 | F9 (hardening + go-live) |

Total ~6 meses calendario.

---

## 6. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| **CxP atrasado respecto a Almacén** en eventos de variante B | Almacén corre con stubs hasta CxP esté listo. F3-PR2/PR3 quedan como wireup pendiente sin bloquear el resto. |
| **Re-localización del placeholder rompe Administración** | F1-PR3 coordinado con Administración. Validación previa: `grep -r "compartido.almacenes" backend/src/` para detectar consumidores. |
| **Saldo materializado se desincroniza** si una transacción falla parcial | Trigger BEFORE INSERT en la misma transacción. Tests de integración cubren rollback. |
| **Migración SAP catálogo de artículos** con datos inconsistentes | Script de validación previo + criterio "no migrar sin movimiento 2 años". Reporte de inconsistencias al área antes de aplicar. |
| **Inventario anual con dos turnos** complica el bloqueo de salidas | F7-PR7 incluye comunicación clara al usuario al intentar salida bloqueada. Política: programar conteo anual en ventana de menor actividad (Carlos elige fecha). |
| **Devoluciones a proveedor sin acuerdo claro con CxP** | F6 va después de F5 — el ciclo CxP→Almacén ya está rodado. Sesión semanal con CxP durante F6. |
| **Volumen real >> 5K mov/mes** | A1 documenta el rollback (particionamiento por mes). Métricas desde F2. |

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Plan de 10 fases sobre el 01-diseno v1. Cronograma 6 meses con equipo 2BE+1FE.
