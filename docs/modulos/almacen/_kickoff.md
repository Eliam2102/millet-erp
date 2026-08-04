# Kickoff — Módulo Almacén (`Millet.Almacen`)

> **Propósito:** este archivo es el punto de arranque para una ventana de Claude Code dedicada al backend del módulo Almacén. Léelo completo antes de tocar código. Sigue el plan secuencial; respeta los **STOP** marcados; reporta tras cada PR mergeado.
>
> **Carril:** Almacén es el **carril principal** en el paralelo Almacén + CxP. Publica los contratos de eventos críticos que CxP consume. No hay carril que dependa de ti más allá de los 2 STOP indicados.
>
> **Fecha:** 2026-05-22.

---

## 0. Contexto rápido del módulo

Antes de arrancar, ten claro:

- **Qué hace este módulo:** administra el inventario físico bajo responsabilidad del Jefe Almacén — recepciones, salidas, devoluciones, inventario físico, reportes. Reemplaza al Portal Millet (`192.168.1.38/PortalSap/`) completo.
- **Scope ampliado:** incluye materiales directos no-vidrio (interlayer, silicones, pinturas). Solo el vidrio crudo queda fuera (continúa en A+W).
- **Usuario primario:** Carlos Burgos (Jefe Almacén). Está esperando deprecar el Portal Millet.

**Lectura obligatoria antes del primer PR (en este orden):**

1. [00-levantamiento.md](00-levantamiento.md) — mapa funcional (787L).
2. [01-diseno.md](01-diseno.md) — diseño técnico (858L). Asunciones A1-A18 en §3.
3. [02-plan-implementacion.md](02-plan-implementacion.md) — fases.
4. [03-pr-breakdown.md](03-pr-breakdown.md) — 13 PRs concretos.
5. [04-cuidados-infra.md](04-cuidados-infra.md) — cuidados operativos.

**Lectura cruzada (cuando llegues a fases de integración):**

- [`docs/modulos/cuentas-por-pagar/00-levantamiento.md`](../cuentas-por-pagar/00-levantamiento.md) §11.6 — contratos de eventos canónicos cross-módulo.
- [`docs/modulos/compras-ordenes-compra/01-diseno.md`](../compras-ordenes-compra/01-diseno.md) §8.5–8.6 — convención canónica de naming `{Agregado}{Verbo}Event`.

---

## 1. Convenciones operativas

### Auto-mode N2

**Activo** para branches `almacen/*`. En esos branches, sin pedir permiso explícito:

- `git add`, `git commit`, `git push origin <branch>`
- `gh pr create`, `gh pr edit`, `gh pr ready`, `gh pr checks`
- `gh pr merge --squash --delete-branch` **solo si CI verde**
- Cleanup: `git branch -d`, `git fetch`, `git checkout main`, `git pull`

El hook `.claude/hooks/validate-auto-merge.ps1` valida CI antes del merge. Si CI está rojo, el hook bloquea con `exit 2` — reportar a Eduardo el estado del check.

### Naming de branches

- Backend: `almacen/f<fase>-<slug-corto>` (ej. `almacen/f2-movimientos-y-saldos`).
- Frontend: `almacen-fe/f<fase>-<slug>` (otra ventana cuando arranque).

### Granularidad de PRs

**Política consolidada** (`feedback_pr_granularidad.md`): PRs S-M (200-800 líneas netas). Aislar como PR único solo cuando hay riesgo real (migraciones cross-table, transacciones cross-puerto, middleware en hot path, integraciones externas). Cada aislamiento documenta razón.

El [03-pr-breakdown.md](03-pr-breakdown.md) ya está consolidado a 13 PRs. **No partas los PRs en sub-PRs** salvo que un PR exceda 800 líneas netas al codificarlo — en ese caso, partir documentando la razón.

### Convenciones técnicas

Heredadas del proyecto (CLAUDE.md):

- Hexagonal + CQRS con MediatR.
- EF Core, schema `almacen` (ADR-0030).
- FluentValidation por comando (ADR-0018).
- Mapster para DTOs.
- Serilog estructurado.
- `record` para DTOs/VOs, `sealed` por defecto, nullable habilitado.
- Naming canónico de eventos: `{Agregado}{Verbo}Event`.
- `PLATFORM-TODO(<id>)` en cada stub temporal (ADR-0031).

### Después de cada PR mergeado

Reporta a Eduardo en una línea:

```
✅ <ID-PR> mergeado (#<num-gh-pr>). <cambios principales 1-2 frases>.
Siguiente: <ID-PR siguiente> — <título>.
```

---

## 2. Plan secuencial

PRs del 03-pr-breakdown ejecutados en orden. **STOP** indica punto de sincronización con la ventana de CxP.

### Bloque A — Foundation + Re-localización (independiente)

```
F0-PR1  →  Foundation
F1-PR1  →  Catálogo y endpoints (touch a compartido.almacenes — coordinar con Administración)
F1-PR2  →  DROP placeholder (pre-validar `rg "compartido\.almacenes" backend/src/` = 0 hits)
F1-PR3  →  Seed inicial SAP
```

**Sin dependencias externas a CxP. Avanza sin parar.**

### Bloque B — Recepción base (publica contratos críticos)

```
F2-PR1  →  Movimientos + saldos materializados (trigger PG)
F2-PR2  →  Recepción Variante A + evento OcRecepcionRegistradaEvent publicado
```

**Importante:** F2-PR2 publica el evento que CxP F5-PR1 consume. Mergea F2-PR2 antes de que CxP llegue a su F5.

### 🛑 STOP #1 — antes de F3-PR1 (NO antes de F3-PR2)

Verifica que **CxP F3-PR2 esté mergeado en main**.

```bash
gh pr list --base main --search "cxp/f3-eventos-outbox merged" --state merged
# o más simple: git log origin/main --oneline | grep "cxp/f3-eventos-outbox"
```

CxP F3-PR2 publica los contratos `FacturaProveedorRegistradaEvent` y `DiferenciaPrecioFacturaDetectadaEvent` que tú vas a consumir en F3-PR1. Si no está mergeado:

- **Avísame a Eduardo** con el folio del último PR tuyo mergeado y el estado de CxP.
- **No avances** a F3-PR1.
- Mientras esperas, puedes trabajar en **F4-PR1 (salida normal)** o **F5-PR1 (vale + dev interna)** que son independientes.

Si está mergeado, continúa.

### Bloque C — Variante B + Reservas + Salidas + Vale

```
F3-PR1  →  Recepción Variante B (suscribe FacturaProveedorRegistradaEvent + DiferenciaPrecio)
F3-PR2  →  Reservas de stock (A19) — agregado ReservaStock + cantidad_reservada + IAlmacenReservaPort
F4-PR1  →  Salida normal con RQ (consume reserva si existe)
F5-PR1  →  Vale + Devoluciones internas
```

**F3-PR2 es NUEVO** (decisión 2026-05-22, opción B del teardown de Compras). Es independiente de CxP — solo necesita que F2-PR2 esté mergeado (que ya lo está en este punto). Publica `IAlmacenReservaPort` que Compras consumirá en el PR de teardown.

**Independiente de CxP** después de F3-PR1.

### 🛑 STOP #2 — antes de F6-PR1

Verifica que **CxP F6-PR2 esté mergeado en main**.

CxP F6-PR2 publica el contrato `NotaCreditoFiscalDevolucionRecibidaEvent` que tú vas a consumir en F6-PR1. Si no está mergeado:

- Avísame a Eduardo.
- Mientras esperas, puedes adelantar **F7-PR1 (captura sin sesgo)** o **F7-PR2 (recuento + aprobación)** que son completamente independientes.

Si está mergeado, continúa.

### Bloque D — Devolución a proveedor (ciclo bidireccional)

```
F6-PR1  →  Devolución a proveedor (publica OcDevolucionRegistradaEvent; suscribe NotaCreditoFiscalDevolucionRecibidaEvent)
```

**Importante:** F6-PR1 publica el evento que CxP F6-PR3 consume. Después de tu merge, CxP puede avanzar.

### Bloque E — Inventario físico + reportes + hardening (independiente)

```
F7-PR1  →  Captura sin sesgo (lógica de seguridad UX; tests E2E críticos)
F7-PR2  →  Recuento + aprobación por monto
F7-PR3  →  Bloqueo de salidas durante inventario anual
F8-PR1  →  Reportes ALFAK + MP-CNK (coordinar <ReporteShell> con CxP si CxP llega primero)
F8-PR2  →  Cierre de mes + validación periodo cerrado
F9-PR1  →  Hardening + runbook + go-live checklist
```

**Sin dependencias externas. Avanza sin parar.**

---

## 3. Resumen de puntos de sincronización

| # | Tu PR | Espera a | Trabajo alternativo mientras esperas |
|---|---|---|---|
| 1 | F3-PR1 | CxP F3-PR2 mergeado | F4-PR1, F5-PR1 |
| 2 | F6-PR1 | CxP F6-PR2 mergeado | F7-PR1, F7-PR2 |

## 4. Qué publicas que CxP necesita

Para que la ventana de CxP avance sin atorarse:

| Tu PR (publica) | CxP PR (suscribe) |
|---|---|
| F2-PR2 (`OcRecepcionRegistradaEvent`) | CxP F5-PR1 |
| F6-PR1 (`OcDevolucionRegistradaEvent`) | CxP F6-PR3 |

**Si CxP me reporta "esperando contrato de Almacén"**, prioriza el PR correspondiente.

---

## 5. Teardown de stubs en Compras (cierre coordinado)

El módulo **Compras** tiene 6 stubs `InMemory*Port` y 1 tabla provisional que cierran cuando publiques los eventos reales y expongas el puerto público `IAlmacenSaldoQueryPort`.

**Tu trabajo específico:**

- **F2-PR2** incluye exponer `IAlmacenSaldoQueryPort` en `Almacen.Domain.Ports.Public` (Open Host Service). Compras usará este puerto para reemplazar `InMemoryConsultarStockPort`. Ver §6.2 del [01-diseno.md](01-diseno.md).
- **F2-PR2** publica `OcRecepcionRegistradaEvent` real (reemplaza `OcBorradorStub` en Compras).
- **F3-PR2** (NUEVO — A19) expone `IAlmacenReservaPort` que Compras usará para reemplazar `InMemoryReservarStockPort` e `InMemoryLiberarReservaPort`. Decisión 2026-05-22 opción B del teardown.
- **F4-PR1** publica `SalidaRequisicionRegistradaEvent` (reemplaza `InMemoryGenerarMovimientoSalidaPort` en Compras).
- **F6-PR1** publica `OcDevolucionRegistradaEvent` (activa `OcDevolucionRegistradaListener` en Compras).

**Acción posterior fuera de Almacén:** después de que tus 4 PRs (F2-PR2, F3-PR2, F4-PR1, F6-PR1) estén mergeados — más los de CxP equivalentes — se ejecuta un PR transversal en el módulo **Compras** que borra los stubs. Doc: [`compras-ordenes-compra/_teardown-stubs-almacen-cxp.md`](../compras-ordenes-compra/_teardown-stubs-almacen-cxp.md).

**No tienes que hacer ese PR de teardown** — es trabajo en branch `compras/oc-*`, no `almacen/*`. Pero **avísame a Eduardo** cuando tu F6-PR1 mergee, porque entonces Compras puede ejecutar el teardown completo.

---

## 6. Coordinación con módulo Administración (F1)

F1-PR1, F1-PR2, F1-PR3 tocan tabla compartida `compartido.almacenes`:

- F1-PR1: aditivo (copia datos a `almacen.almacenes`).
- F1-PR2: **DROP** del placeholder. Antes de mergear:
  - `rg "compartido\.almacenes" backend/src/` → debe retornar **0 hits**.
  - Smoke tests de Compras + CxP + Administración pasan.
- F1-PR3: seed con datos reales desde SAP.

Si encuentras consumidores residuales de `compartido.almacenes` antes de F1-PR2, **detente y avísame a Eduardo** — es un blocker.

---

## 7. Primer comando

```bash
git checkout main
git pull
git checkout -b almacen/f0-foundation

# Empieza por F0-PR1 según el 03-pr-breakdown §Fase 0
```

Si tienes dudas sobre algún PR específico antes de implementarlo, lee el §correspondiente del [01-diseno.md](01-diseno.md) primero. Las asunciones A1-A18 (§3 del 01-diseno) son tu referencia para decisiones no triviales.

---

## 8. Si algo se sale del plan

Avísame a Eduardo **inmediatamente** si:

- Un PR excede 800 líneas netas durante la implementación (necesita partirse).
- Encuentras una decisión técnica no cubierta por las 18 asunciones del 01-diseno.
- El backend de CxP me reporta un atraso que afecta tus STOPs.
- Tests E2E críticos (captura sin sesgo, saldo materializado) fallan de forma no obvia.
- Detectas inconsistencia en los contratos de eventos cross-módulo.

No improvises decisiones técnicas mayores sin confirmar.

---

**Listo. Empieza por F0-PR1.**
