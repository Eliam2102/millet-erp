# Teardown de stubs cross-módulo en Compras — cierre con Almacén y CxP

> **Propósito:** documentar el trabajo pendiente en el módulo Compras que cierra cuando los módulos Almacén y CxP existen en runtime. Cierra el ticket `<StubsTeardown>` mencionado en [`compras-requisiciones/01-diseno.md`](../compras-requisiciones/01-diseno.md) §8.6 y [`compras-ordenes-compra/01-diseno.md`](01-diseno.md) §12.
>
> **Estado:** plan listo para ejecución una vez que las dependencias estén mergeadas.
>
> **Fecha:** 2026-05-22.

---

## 0. Por qué este doc existe

Compras (Requisiciones + OC) está en main desde antes de que existieran Almacén y CxP. Para no bloquearse, el módulo introdujo:

- **6 stubs `InMemory*Port`** en `backend/src/Compras/Infrastructure/Stubs/`.
- **Tabla provisional** `compras.oc_borrador_stub`.
- **Flag** `Compras:UseStubs` + clases `ComprasStubsOptions` y `ComprasStubsServiceCollectionExtensions`.
- **Listeners cross-BC** ya cableados (no son stubs — son consumers reales que simplemente esperan eventos).

Los stubs se activan solo en `Development` y `Test` (con guardia que falla el bootstrap si se intentan activar en `Production`). Ejercitan el flujo de Compras sin requerir los submódulos hermanos.

Cuando Almacén y CxP están en runtime, este trabajo cierra el ticket: borra los stubs, conecta adapters reales, y deja Compras 100% productivo cross-módulo.

---

## 1. Inventario de stubs y listeners

### 1.1 Stubs activos en `backend/src/Compras/Infrastructure/Stubs/`

| Stub | Puerto que implementa | Consumidor en Compras | Cierre con |
|---|---|---|---|
| `InMemoryConsultarStockPort` | `IConsultarStockPort` | OC al capturar línea ("Existencia actual del material") y bandeja de stock | **Almacén** — adapter real consume `IAlmacenSaldoQueryPort` (puerto público que Almacén expone en F2-PR2). |
| `InMemoryReservarStockPort` | `IReservarStockPort` | Requisiciones al transmitir (reserva stock contra material) | ✅ **Almacén F3-PR2** — adapter real `AlmacenReservaAdapter` delega a `IAlmacenReservaPort` (decisión 2026-05-22 opción B). |
| `InMemoryLiberarReservaPort` | `ILiberarReservaPort` | Requisiciones al cancelar RQ con reserva activa | ✅ **Almacén F3-PR2** — mismo adapter real. |
| `InMemoryGenerarMovimientoSalidaPort` | `IGenerarMovimientoSalidaPort` | Requisiciones cuando una RQ se "completa" y genera salida en Almacén | **Almacén** — reemplazado por el evento `SalidaRequisicionRegistradaEvent` que Almacén publica desde F4-PR1. El puerto en Requisiciones se vuelve obsoleto (Almacén es quien dispara el movimiento al recibir la RQ, no Requisiciones empuja). Ver §3. |
| `InMemoryGenerarSolicitudCompraPort` | `IGenerarSolicitudCompraPort` | Requisiciones cuando se aprueba RQ que genera OC | Ya cubierto por OC. Verificar si sigue activo en código antes de borrar. |
| `OcBorradorStub` + tabla `compras.oc_borrador_stub` + `OcBorradorStubConfiguration` | Simula OC para que Requisiciones tenga algo a qué referenciar antes de existir OC | Listeners `OcRecepcionRegistradaListener`, `OcCerradaListener` de Requisiciones | Ya cubierto por OC mergeado. Verificar si la tabla sigue poblándose en dev/test. |

### 1.2 Configuración y orquestación

| Pieza | Acción al cierre |
|---|---|
| `Compras:UseStubs` (flag de appsettings) | Borrar de `appsettings.Development.json` y `appsettings.Test.json`. |
| `ComprasStubsOptions.cs` | Borrar archivo. |
| `ComprasStubsServiceCollectionExtensions.cs` | Borrar archivo. |
| Llamada a `services.AddComprasStubs(...)` en `Program.cs` | Borrar líneas. |
| Guardia que falla bootstrap si stubs se activan en Production | Borrar (no aplica sin stubs). |

### 1.3 Listeners cross-BC (NO son stubs — se activan solos)

Estos listeners ya están en código (consumers MediatR + outbox suscritos a Service Bus). Simplemente esperan a que el evento empiece a publicarse.

| Listener | Se activa con | Cuándo |
|---|---|---|
| `OcRecepcionRegistradaListener` (actualiza `CantidadRecibida` + `SubEstadoRecepcion`) | Almacén publica `OcRecepcionRegistradaEvent` real (no `OcBorradorStub`) | **Almacén F2-PR2** |
| `OcDevolucionRegistradaListener` (decrementa `CantidadRecibida`) | Almacén publica `OcDevolucionRegistradaEvent` | **Almacén F6-PR1** |
| `FacturaProveedorRegistradaListener` (actualiza `CantidadFacturada` + `SubEstadoFacturacion`) | CxP publica el evento | **CxP F3-PR2** |
| `FacturaProveedorRechazadaPorToleranciaListener` (registra rechazo + bandera) | CxP publica el evento | **CxP F3-PR2** |
| `FacturaProveedorCanceladaListener` (decrementa `CantidadFacturada`) | CxP publica el evento | **CxP F3-PR2** |
| `NotaCreditoProveedorRegistradaListener` (decrementa `CantidadFacturada`) | CxP publica el evento | **CxP F6-PR1** |
| `DiferenciaPrecioFacturaDetectadaListener` (informativo) — **nuevo** según consolidación de eventos del 2026-05-22 | CxP publica el evento | **CxP F3-PR2** — **a crear si no existe en código** |
| `PagoFacturaProveedorListener` (actualiza `SubEstadoPago`) | Tesorería publica el evento | Tesorería (post-MVP, no en este plan) |

> Si `DiferenciaPrecioFacturaDetectadaListener` no existe aún en código de Compras (es un evento nuevo introducido por la consolidación cross-módulo del 2026-05-22), crearlo es parte del PR de teardown.

---

## 2. Decisiones pendientes

### 2.1 ~~Reservas de stock~~ — ✅ Decidido OPCIÓN B (2026-05-22)

Eduardo confirmó **opción B**: reservas de stock entran al MVP de Almacén.

**Implicación implementada en Almacén:**

- ✅ Columna `cantidad_reservada` en `saldos_inventario` + columna generada `cantidad_disponible = cantidad - cantidad_reservada`.
- ✅ Tabla nueva `almacen.reservas_stock` con estados `Activa` / `Consumida` / `Liberada`.
- ✅ Comandos `ReservarStockCommand` y `LiberarReservaCommand` en Almacén.
- ✅ Eventos `StockReservadoEvent` y `StockLiberadoEvent` publicados.
- ✅ Puerto público `IAlmacenReservaPort` expuesto en `Almacen.Domain.Ports.Public`.
- ✅ Lógica en F4-PR1 (salida normal): si la RQ tiene reserva activa, la consume (decrementa `cantidad` y `cantidad_reservada` simultáneamente). Si no, consumo directo.

Trabajo en Almacén: **F3-PR2 nuevo** (~1 semana, M, aislado por riesgo). Ver [`almacen/03-pr-breakdown.md`](../almacen/03-pr-breakdown.md).

**Implicación para el teardown de Compras:**

- Borrar `InMemoryReservarStockPort.cs` + `InMemoryLiberarReservaPort.cs`.
- Crear adapter real `AlmacenReservaAdapter` en `Compras.Infrastructure.Adapters` que implementa `IReservarStockPort` y `ILiberarReservaPort` delegando a `IAlmacenReservaPort`.
- El flujo de Requisiciones se mantiene como hoy — Compras sigue llamando a `IReservarStockPort` desde Requisiciones; el adapter real delega.

### 2.2 `IGenerarMovimientoSalidaPort`

Hoy en Requisiciones, cuando una RQ se "completa", llama a este puerto para generar un movimiento de salida. En el modelo nuevo:

- Almacén es quien decide cuándo registrar la salida (al surtir físicamente, no cuando se cierra la RQ).
- Almacén publica `SalidaRequisicionRegistradaEvent` desde F4-PR1.
- Requisiciones consume el evento y marca la RQ como surtida.

Por lo tanto, **el puerto `IGenerarMovimientoSalidaPort` ya no aplica**. Se puede borrar el stub + el puerto + el llamado en Requisiciones, **siempre y cuando** Requisiciones se actualice para consumir el evento (un nuevo listener `SalidaRequisicionRegistradaListener` en Compras).

**Trabajo adicional en Compras:** crear `SalidaRequisicionRegistradaListener` que marque la RQ como surtida al recibir el evento de Almacén. Esto va en el PR de teardown.

---

## 3. PR de teardown — propuesta

**Branch:** `compras/oc-teardown-stubs-almacen-cxp`

**Tamaño estimado:** M (500-800 líneas netas).

**Auto-mode:** activo (branch `compras/oc-*`).

**Alcance consolidado:**

1. **Adapter real `AlmacenSaldoAdapter`** en `Compras.Infrastructure.Adapters` que implementa `IConsultarStockPort` delegando a `IAlmacenSaldoQueryPort` (inyectado desde Almacén F2-PR2). Tests de integración.
2. **Adapter real `AlmacenReservaAdapter`** que implementa `IReservarStockPort` e `ILiberarReservaPort` delegando a `IAlmacenReservaPort` (inyectado desde Almacén F3-PR2). Tests concurrentes (2 reservas paralelas — solo 1 gana).
3. **Borrar** `InMemoryConsultarStockPort.cs`, `InMemoryReservarStockPort.cs`, `InMemoryLiberarReservaPort.cs`. Registrar los adapters reales en `Program.cs` reemplazando los stubs.
4. **Borrar `InMemoryGenerarMovimientoSalidaPort.cs`** + el llamado en Requisiciones. Crear `SalidaRequisicionRegistradaListener` que consume el evento de Almacén (publicado en F4-PR1) y marca la RQ surtida.
5. **Borrar `InMemoryGenerarSolicitudCompraPort.cs`** (si confirmado obsoleto post-OC).
6. **Borrar `OcBorradorStub.cs` + `OcBorradorStubConfiguration.cs`**. Migración `DROP TABLE compras.oc_borrador_stub`.
7. **Crear `DiferenciaPrecioFacturaDetectadaListener`** (si no existe) que reciba el evento de CxP y registre informativamente en el log de OC.
8. **Eliminar config**: `Compras:UseStubs`, `ComprasStubsOptions.cs`, `ComprasStubsServiceCollectionExtensions.cs`. Quitar `services.AddComprasStubs(...)` de `Program.cs`. Eliminar guardia de Production.
9. **Borrar comentarios** `// PLATFORM-TODO(<StubsTeardown>)` en código de Compras (`rg "PLATFORM-TODO\(<StubsTeardown>\)" backend/src/Compras` → 0 hits).
10. **Tests E2E** del flujo completo (sin stubs):
    - RQ → reserva stock → OC → Recepción de Almacén → consumo de reserva al surtir → Factura de CxP → todos los sub-estados de OC actualizados correctamente.
    - Bandeja de stock muestra cantidades reales (cantidad, reservada, disponible) de Almacén.
    - Cancelación de RQ libera la reserva sin afectar `cantidad`.

**Mergeable cuando:**

- ✅ Almacén F2-PR2 mergeado (publica `OcRecepcionRegistradaEvent` real y expone `IAlmacenSaldoQueryPort`).
- ✅ Almacén F3-PR2 mergeado (expone `IAlmacenReservaPort` — A19).
- ✅ Almacén F4-PR1 mergeado (publica `SalidaRequisicionRegistradaEvent` con consumo de reserva).
- ✅ Almacén F6-PR1 mergeado (publica `OcDevolucionRegistradaEvent`).
- ✅ CxP F3-PR2 mergeado (publica `FacturaProveedorRegistradaEvent`, `FacturaProveedorRechazadaPorToleranciaEvent`, `FacturaProveedorCanceladaEvent`, `DiferenciaPrecioFacturaDetectadaEvent`).
- ✅ CxP F6-PR2 mergeado (publica `NotaCreditoProveedorRegistradaEvent`).

**Cronograma:**

Con el plan de Almacén + CxP en paralelo, las dependencias listadas arriba se cierran alrededor del **Mes 3-4** del cronograma. El PR de teardown se ejecuta entonces.

---

## 4. Lo que NO entra en este PR de teardown

- **Tesorería**: el listener `PagoFacturaProveedorListener` queda inactivo. Cierre futuro cuando exista Tesorería.
- **Notificaciones**: los eventos `OrdenCompraEnviadaAAutorizacionEvent`, `OrdenCompraAutorizadaEvent`, `OrdenCompraRechazadaEvent` se publican pero no envían email. Cierre futuro cuando exista módulo Notificaciones (ADR-0026).
- **CollaborationHub frontend (UF8-PR1)**: stub frontend de `useCollaboration` / `<CollaborationIndicator/>`. Cierre independiente, no afecta backend.

---

## 5. Validación post-teardown

Después de mergear el PR de teardown, ejecutar:

```bash
# 1. No deben quedar archivos de stubs
ls backend/src/Compras/Infrastructure/Stubs/ 2>&1   # debe estar vacío o no existir

# 2. No deben quedar PLATFORM-TODO(<StubsTeardown>)
rg "PLATFORM-TODO\(<StubsTeardown>\)" backend/src/

# 3. Flag eliminada
grep -r "Compras:UseStubs" backend/   # debe devolver 0 hits

# 4. Tabla droppeada
psql -d millet_dev -c "\dt compras.oc_borrador_stub"   # debe decir 'no relation'

# 5. Tests E2E pasan
dotnet test --filter Category=E2E
```

---

## 6. Riesgos del teardown

| Riesgo | Mitigación |
|---|---|
| **Adapter real de stock más lento** que el InMemory en dev | Profiling antes de mergear; índice `ix_saldos_con_stock` en Almacén ya cubre. |
| **Race condition** entre evento de Almacén y consulta sincrónica de stock | El puerto `IAlmacenSaldoQueryPort` lee de `saldos_inventario` que se actualiza en la misma transacción que el movimiento (trigger PG). Consistencia transaccional, no eventual. |
| **Listener nuevo `DiferenciaPrecioFacturaDetectadaListener` no existe en código** | Crearlo como parte del PR; idempotencia con `eventos_procesados`. |
| **Tests de Compras dependen de stubs** | Auditar tests + reemplazar por mocks de los puertos reales (no del InMemory). |
| ~~**Reservas (§2.1) sin decisión clara**~~ | ✅ **Cerrado 2026-05-22**: opción B (reservas en MVP de Almacén F3-PR2). |

---

## 7. Owner y aprobaciones

- **Owner técnico:** Eduardo (único dev del proyecto).
- **Reviewer funcional:** Rodrigo Chay (Jefe Compras) para la decisión de reservas + smoke test del flujo RQ→OC.
- **Auto-mode:** activo en branch `compras/oc-teardown-stubs-almacen-cxp`.

---

## Rev.

- **2026-05-22 — v2** — Decisión de reservas cerrada: opción B (entran al MVP de Almacén como F3-PR2). Alcance del PR de teardown actualizado para incluir `AlmacenReservaAdapter`. Stubs `InMemoryReservar/LiberarReserva` ahora sí se borran.
- **2026-05-22 — v1** — Plan inicial del teardown.
