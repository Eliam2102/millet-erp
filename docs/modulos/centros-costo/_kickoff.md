# Kickoff — Módulo Centros de Costo (`Millet.CentrosCosto`)

> Guía operativa para las sesiones que implementen el módulo. Leer junto
> con [`00-levantamiento.md`](00-levantamiento.md) (fuente, historia §7 y
> gaps) y [`03-pr-breakdown.md`](03-pr-breakdown.md).

---

## 0. Contexto rápido

Catálogo jerárquico **Dim1 → Dim2 → Dim3** (+ grupos GrupoDim2/GrupoDim3)
que mata los GUIDs tecleados a mano en documentos: el usuario captura UN
solo campo ("Máquina", Dim3) por línea. **Totalmente separado** de los
catálogos compartidos del ERP (decisión 2026-07-16, levantamiento §7.4 —
el CONKAL de CeCo no se relaciona con la sucursal Conkal del ERP).
Autoridad funcional: **Contabilidad**. Solo clasifica (presupuesto futuro;
reportes = Fase F pendiente).

**Estado:** Fase A mergeada (#591/#594/#599) con los nombres del modelo
anterior; #603 (siembra cross-schema) cerrado sin mergear. Lo siguiente es
el **refactor Dim (PR-4)**.

## 1. Convenciones operativas

### Auto-mode y branches
- Ramas backend `centros-costo/pr{N}-{slug}`, frontend
  `centros-costo-fe/pr{N}-{slug}` — ✅ allowlist del hook
  `validate-auto-merge` (auto-mode N2: merge con CI verde + rama al día).

### Técnicas (no negociables)
- **Separación total**: cero lecturas/escrituras fuera del esquema
  `centros_costo`. Sin read-ports a otros módulos hasta la Fase E (y ahí
  CeCo es el SERVIDOR del puerto, no el cliente).
- **Nomenclatura de código Dim pura** (Dim1/Dim2/Dim3, GrupoDim2/GrupoDim3,
  tablas `dim1`…, API `nodoTipo=dim1|dim2`); la UI traduce por contexto
  ([`05-frontend-diseno.md`](05-frontend-diseno.md) §0). Gate de PR:
  grep de vocabulario viejo (`SucursalCeCo|SucursalCentroCosto|Subgrupo`)
  = 0 en `backend/src/CentrosCosto`.
- **Nunca un GUID de cara al usuario.**
- Padre inmutable (reubicar = baja + alta); baja lógica en cascada
  (ADR-0049); claves únicas globales.
- Alcance **congelado en máquinas** (`usuario → dim3_id`, una columna):
  marcar niveles = atajo de captura que expande; sin re-evaluación en
  vivo; tri-estado calculado, nunca almacenado (diseño §7).
- Siembra = migración **single-schema** idempotente (sin-target + guard
  final de conteos; lección #501, [`04-cuidados-infra.md`](04-cuidados-infra.md) §2).
- Idempotency-Key UUID v4 por submit; ETag/If-Match explícito (semántica
  Cajas, implementada en #594); Problem Details.

### Después de cada PR mergeado
Actualizar memoria de progreso + `Rev.` del doc tocado si cambió una
decisión.

## 2. Plan secuencial

**PR-4 — Refactor Dim** (`centros-costo/pr4-dims-propias`).

### 🛑 STOP #1 — antes del gate de PR-4
**Runbook de limpieza de millet_dev**: borrar la siembra local
nunca-mergeada de #603 (árbol `0000000c-*`, las 5 sucursales CKL–PIN de
`compartido.sucursales` y los 2 registros de `__EFMigrationsHistory`).
La migración de renombrado asume tablas con datos solo-del-modelo-nuevo.

**PR-5 — Siembra single-schema** → **PR-6 — Alcance** (la pieza SIN molde;
el diseño §7 es el contrato, no se re-decide semántica en el PR).

### 🛑 STOP #2 — antes de PR-7/PR-8 (Fase E)
Tocan Almacén y Compras (equipo activo): coordinar con Eduardo que no haya
trabajo en vuelo sobre `NuevaSalidaSheet`/`LineaInlineForm`, y resolver
CC-G4 documento por documento (qué documentos muestran los niveles
heredados — la CAPTURA ya está cerrada: un campo "Máquina").

**FE:** FE-PR1/FE-PR2 (configuración) tras PR-5; FE-PR3 (asignación) tras
PR-6; FE-PR4/FE-PR5 (documentos) tras PR-7/PR-8.

## 3. Gates funcionales pendientes

| Gate | Bloquea | Owner |
|---|---|---|
| Runbook limpieza millet_dev (STOP #1) | gate de PR-4 | Victor |
| CC-G4: qué documentos muestran niveles heredados | PR-7/PR-8 (por documento) | Eduardo/Contabilidad |
| CC-G5: usuario sin asignación = lista vacía (v1) | FE-PR3 | Contabilidad |
| Fase F (reportes): 3 preguntas abiertas ([`02-plan`](02-plan-implementacion.md) §3-F) | Fase F completa | Eduardo/Contabilidad |

CC-G1/G2/G3 del modelo anterior: obsoletos o resueltos por diseño
(levantamiento §8). PR-4/PR-5/PR-6 son arrancables hoy (tras el STOP #1).
