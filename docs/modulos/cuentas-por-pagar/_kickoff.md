# Kickoff — Módulo Cuentas por Pagar (`Millet.CuentasPorPagar`)

> **Propósito:** este archivo es el punto de arranque para una ventana de Claude Code dedicada al backend del módulo CxP. Léelo completo antes de tocar código. Sigue el plan secuencial; respeta los **STOP** marcados; reporta tras cada PR mergeado.
>
> **Carril:** CxP es el **carril secundario** en el paralelo Almacén + CxP. Consume contratos de eventos publicados por Almacén. Tienes 2 STOPs esperando a Almacén; durante esos STOPs hay trabajo alternativo disponible.
>
> **Fecha:** 2026-05-22.

---

## 0. Contexto rápido del módulo

Antes de arrancar, ten claro:

- **Qué hace este módulo:** administra el ciclo de vida del pasivo con proveedores — recepción del CFDI, captura y validación de la factura, conciliación con OC, autorización, gestión de anticipos, notas de cargo, NC del proveedor, mantenimiento del saldo por proveedor. Incluye 4 variantes sin OC (Caja Chica, Viáticos, TC Empresarial, Aduanales).
- **Sub-módulo más complejo del scope:** Tarjetas de Crédito Empresariales (TC). Tiene anexo técnico dedicado.
- **Lo que NO hace:** ejecutar pagos (Tesorería), registrar REPP (Tesorería), generar OCs (Compras), mantener master de proveedores (DatosMaestros).

**Lectura obligatoria antes del primer PR (en este orden):**

1. [00-levantamiento.md](00-levantamiento.md) — mapa funcional (1006L).
2. [01-diseno.md](01-diseno.md) — diseño técnico (801L). Asunciones A1-A22 en §3.
3. [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md) — diseño TC (776L). Lectura **obligatoria antes de F7**, no antes.
4. [02-plan-implementacion.md](02-plan-implementacion.md) — fases.
5. [03-pr-breakdown.md](03-pr-breakdown.md) — 22 PRs concretos.
6. [04-cuidados-infra.md](04-cuidados-infra.md) — cuidados operativos.

**Lectura cruzada (cuando llegues a fases de integración):**

- [`docs/modulos/almacen/00-levantamiento.md`](../almacen/00-levantamiento.md) §11 — contratos de eventos cross-módulo.
- [`docs/modulos/compras-ordenes-compra/01-diseno.md`](../compras-ordenes-compra/01-diseno.md) §8.5–8.6 — convención canónica de naming `{Agregado}{Verbo}Event`.

---

## 1. Convenciones operativas

### Auto-mode N2

**Activo** para branches `cxp/*`. En esos branches, sin pedir permiso explícito:

- `git add`, `git commit`, `git push origin <branch>`
- `gh pr create`, `gh pr edit`, `gh pr ready`, `gh pr checks`
- `gh pr merge --squash --delete-branch` **solo si CI verde**
- Cleanup: `git branch -d`, `git fetch`, `git checkout main`, `git pull`

El hook `.claude/hooks/validate-auto-merge.ps1` valida CI antes del merge. Si CI rojo, bloquea con `exit 2` — reportar a Eduardo el estado del check.

### Naming de branches

- Backend: `cxp/f<fase>-<slug-corto>` (ej. `cxp/f3-factura-con-oc`).
- Frontend: `cxp-fe/f<fase>-<slug>` (otra ventana cuando arranque).

### Granularidad de PRs

**Política consolidada** (`feedback_pr_granularidad.md`): PRs S-M (200-800 líneas netas). Aislar como PR único solo cuando hay riesgo real. Cada aislamiento documenta razón.

El [03-pr-breakdown.md](03-pr-breakdown.md) ya está consolidado a 22 PRs. **No partas los PRs en sub-PRs** salvo que un PR exceda 800 líneas netas al codificarlo.

### Convenciones técnicas

Heredadas del proyecto (CLAUDE.md):

- Hexagonal + CQRS con MediatR.
- EF Core, schema `cuentas_por_pagar` (ADR-0030).
- FluentValidation por comando.
- Mapster para DTOs.
- Serilog estructurado con masking de RFCs / datos bancarios.
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

PRs del 03-pr-breakdown ejecutados en orden. **STOP** indica punto de sincronización con la ventana de Almacén.

### Bloque A — Foundation + ingestión de CFDIs (independiente)

```
F0-PR1  →  Foundation (csproj + DbContext + schema + permisos + puertos)
F1-PR1  →  CfdiRecibido + ingestión manual + bandeja
F2-PR1  →  FiscalAPI client + workers descarga SAT + refresh estado
F2-PR2  →  Worker mailbox (Microsoft Graph)
```

**Sin dependencias externas a Almacén. Avanza sin parar.**

### Bloque B — Factura con OC + revisión (publica contratos críticos)

```
F3-PR1  →  Factura con OC (captura + tolerancia + endpoints)
F3-PR2  →  Eventos publicados (FacturaProveedorRegistradaEvent, FacturaProveedorRechazadaPorToleranciaEvent, FacturaProveedorAutorizadaEvent, FacturaProveedorCanceladaEvent, DiferenciaPrecioFacturaDetectadaEvent)
F4-PR1  →  Workflow de revisión + motivos + dependencias revisoras
F4-PR2  →  Evidencias de autorización + worker SLA
```

**Importante:** F3-PR2 publica los contratos que Almacén F3-PR1 consume. Mergea F3-PR2 antes de que Almacén llegue a su F3.

### 🛑 STOP #1 — antes de F5-PR1

Verifica que **Almacén F2-PR2 esté mergeado en main**.

```bash
gh pr list --base main --search "almacen/f2-recepcion-variante-a merged" --state merged
# o más simple: git log origin/main --oneline | grep "almacen/f2-recepcion"
```

Almacén F2-PR2 publica el contrato `OcRecepcionRegistradaEvent` que tú vas a consumir en F5-PR1. Si no está mergeado:

- **Avísame a Eduardo** con el folio del último PR tuyo mergeado y el estado de Almacén.
- **No avances** a F5-PR1.
- Mientras esperas, puedes adelantar **F6-PR1 (NC + worker EnEspera)** o **F6-PR2 (anticipos + notas de cargo)** que son independientes de Almacén.

Si está mergeado, continúa.

### Bloque C — Integración cross-módulo + NCs + anticipos

```
F5-PR1  →  Integración Compras + Almacén (suscribe OcRecepcionRegistradaEvent)
F6-PR1  →  Nota de crédito + worker NC EnEspera (publica NotaCreditoProveedorRegistradaEvent)
F6-PR2  →  Anticipos + notas de cargo (publica NotaCargoAutorizadaEvent, AnticipoProveedorCapturadoEvent)
```

F6-PR2 también define el contrato `NotaCreditoFiscalDevolucionRecibidaEvent` (publicado en F6-PR3 cuando aplique). **Mergea F6-PR2 antes de que Almacén llegue a su F6-PR1.**

### 🛑 STOP #2 — antes de F6-PR3

Verifica que **Almacén F6-PR1 esté mergeado en main**.

Almacén F6-PR1 publica el contrato `OcDevolucionRegistradaEvent` que tú vas a consumir en F6-PR3. Si no está mergeado:

- Avísame a Eduardo.
- Mientras esperas, puedes adelantar **F7-PR1 (Comprobaciones base + Caja Chica)** o **F7-PR2 (Aduanales)** que son independientes.

Si está mergeado, continúa.

### Bloque D — Ciclo devolución + Comprobaciones + TC

```
F6-PR3  →  Ciclo devolución Almacén (suscribe OcDevolucionRegistradaEvent; publica NotaCreditoFiscalDevolucionRecibidaEvent)
F7-PR1  →  ComprobacionGastos base + Caja Chica
F7-PR2  →  Aduanales (doble autorización)
F7-PR3  →  Catálogos + Viáticos electrónicos ← LECTURA OBLIGATORIA: 01a-anexo no aplica aquí
F7-PR4  →  TC master + movimientos ← AHORA lee el anexo TC §1-§5 antes de codificar
F7-PR5  →  TC estado de cuenta + parser + match automático ← anexo TC §6, §7
F7-PR6  →  TC cierre + casos especiales ← anexo TC §8
```

**F7 es el bloque más grande del módulo.** Antes de F7-PR4, **lee el [01a-anexo-tc-empresarial.md](01a-anexo-tc-empresarial.md) completo**. Cubre 6 asunciones técnicas D9-D14 que necesitas confirmar con Eduardo + Contabilidad.

### Bloque E — Reportes + Tesorería + Hardening (independiente)

```
F8-PR1  →  Reportes cartera + anticipos + categoría×revisión (coordinar <ReporteShell> con Almacén si Almacén llega primero)
F8-PR2  →  Reportes TC + pasivos Obras
F9-PR1  →  Tesorería wireup (publica PasivoAutorizadoParaPagoEvent; suscribe pagos/REPPs/cancelación)
F10-PR1 →  Tests E2E + performance + audit PLATFORM-TODO
F10-PR2 →  Runbook + go-live checklist
```

**Sin dependencias externas. Avanza sin parar.**

---

## 3. Resumen de puntos de sincronización

| # | Tu PR | Espera a | Trabajo alternativo mientras esperas |
|---|---|---|---|
| 1 | F5-PR1 | Almacén F2-PR2 mergeado | F6-PR1, F6-PR2 |
| 2 | F6-PR3 | Almacén F6-PR1 mergeado | F7-PR1, F7-PR2 |

## 4. Qué publicas que Almacén necesita

Para que la ventana de Almacén avance sin atorarse:

| Tu PR (publica) | Almacén PR (suscribe) |
|---|---|
| F3-PR2 (`FacturaProveedorRegistradaEvent`, `DiferenciaPrecioFacturaDetectadaEvent`) | Almacén F3-PR1 |
| F6-PR2/PR3 (`NotaCreditoFiscalDevolucionRecibidaEvent`) | Almacén F6-PR1 |

**Si Almacén me reporta "esperando contrato de CxP"**, prioriza el PR correspondiente.

---

## 5. Teardown de stubs en Compras (cierre coordinado)

El módulo **Compras** tiene listeners cross-BC ya cableados (`FacturaProveedorRegistradaListener`, `FacturaProveedorRechazadaPorToleranciaListener`, `FacturaProveedorCanceladaListener`, `NotaCreditoProveedorRegistradaListener`) que esperan tus eventos. Apenas tu F3-PR2 mergee con esos eventos publicados, los listeners se activan automáticamente — **no se requiere cambio de código en Compras** para esos.

**Listener nuevo que probablemente NO existe en Compras todavía:** `DiferenciaPrecioFacturaDetectadaListener` (informativo, registra que hubo diferencia de precio en variante B). Es un evento nuevo introducido en la consolidación cross-módulo del 2026-05-22. Si verificas en Compras y no existe, el equipo de Compras lo creará como parte de su PR de teardown.

**Tu trabajo específico:**

- **F3-PR2** publica los 5 eventos de factura proveedor + `DiferenciaPrecioFacturaDetectadaEvent`.
- **F6-PR1** publica `NotaCreditoProveedorRegistradaEvent` (tipo 01/03/07).
- **F6-PR3** publica `NotaCreditoFiscalDevolucionRecibidaEvent` (cierre del ciclo bidireccional con Almacén).

**Acción posterior fuera de CxP:** después de que tus PRs estén mergeados (más los de Almacén equivalentes), se ejecuta un PR transversal en el módulo **Compras** que borra los stubs. Doc: [`compras-ordenes-compra/_teardown-stubs-almacen-cxp.md`](../compras-ordenes-compra/_teardown-stubs-almacen-cxp.md).

**No tienes que hacer ese PR de teardown** — es trabajo en branch `compras/oc-*`, no `cxp/*`. Pero **avísame a Eduardo** cuando tu F3-PR2 mergee, porque entonces Compras puede activar los listeners reales.

---

## 6. Catálogos pendientes del área (pre-go-live)

Antes del go-live (post-F10), el área debe llenar:

1. `motivos_revision` ✅ (seed automático en F4-PR1).
2. `tarjetas_credito` — el área llena con las TC corporativas reales.
3. `aprobadores_limites` — RH llena por usuario × tipo de gasto.
4. `politicas_viaticos` — RH + Dirección por puesto × tipo de destino.
5. `perfiles_parser_banco` — seed `AMEX_MX` en F7-PR5; otros bancos manual.

**Sin estos catálogos, el módulo no opera al 100%.** Empieza a recopilarlos desde F3 (puedes operar con seeds de prueba mientras tanto).

Pendientes específicos del anexo TC (§15.2 del anexo):

- Lista exacta de tarjetas operativas (cuántas, emisores, titulares, límites).
- Política de "movimientos sin CFDI" — ¿hay máximo mensual?
- Tarjetas en otras monedas (USD nativas vs. cargos eventuales).
- Migración histórica — opción 2 propuesta (solo periodo abierto).

---

## 7. Primer comando

```bash
git checkout main
git pull
git checkout -b cxp/f0-foundation

# Empieza por F0-PR1 según el 03-pr-breakdown §Fase 0
```

Si tienes dudas sobre algún PR específico antes de implementarlo, lee el §correspondiente del [01-diseno.md](01-diseno.md) primero. Las asunciones A1-A22 (§3 del 01-diseno) son tu referencia para decisiones no triviales.

---

## 8. Si algo se sale del plan

Avísame a Eduardo **inmediatamente** si:

- Un PR excede 800 líneas netas durante implementación (necesita partirse).
- Encuentras decisión técnica no cubierta por las 22 asunciones del 01-diseno + 6 del anexo TC.
- Almacén me reporta atraso que afecta tus STOPs.
- El parser de FiscalAPI no funciona con el corpus de CFDIs reales que tienes.
- En F7-PR4/PR5, no hay archivo de muestra del banco Amex disponible (bloqueador).
- Tests E2E críticos (captura con OC, conciliación TC) fallan de forma no obvia.

No improvises decisiones técnicas mayores sin confirmar.

---

**Listo. Empieza por F0-PR1.**
