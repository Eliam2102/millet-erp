# PR Breakdown — Módulo Centros de Costo (`Millet.CentrosCosto`)

> **Versión:** 0.2 · **Fecha:** 2026-07-16
> **Basado en:** [`02-plan-implementacion.md`](02-plan-implementacion.md) v0.2.

---

## 0. Cómo leer

Ramas `centros-costo/pr{N}-{slug}` (✅ allowlist del hook
`validate-auto-merge`, auto-mode N2). Un PR por sesión, squash merge, CI
verde + rama al día con main. Los PRs de FE viven en
[`07-frontend-pr-breakdown.md`](07-frontend-pr-breakdown.md).

**Mergeados (Fase A, modelo anterior):** CECO-PR1 #591 (cimiento) ·
CECO-PR2 #594 (CRUD + cascada ADR-0049 + If-Match) · CECO-PR3 #599
(jerarquía lazy, listas, búsqueda). **Cerrado sin mergear:** #603 (siembra
cross-schema del modelo anterior — obsoleta por la separación total,
levantamiento §7.4).

---

## CECO-PR4 — Refactor al modelo Dim (M) · `centros-costo/pr4-dims-propias`

- **Renombres** (nomenclatura del Excel, diseño §4): entidades
  `Dim1`/`Dim2`/`Dim3`/`GrupoDim2`/`GrupoDim3`, tablas
  `dim1`/`dim2`/`dim3`/`grupos_dim2`/`grupos_dim3` (+índices/checks),
  DbSets, commands, queries, DTOs, endpoints
  (`/api/v1/centros-costo/dim1|dim2|dim3|grupos-dim2|grupos-dim3`,
  `nodoTipo=raiz|dim1|dim2`, `/dim3/buscar`) y tests.
- **Colapso del vínculo**: `SucursalCentroCosto` → `Dim1` (−`SucursalId`
  −`ClaveCeCo` → +`Clave` +`Nombre`); `VincularSucursal` → `CrearDim1`
  normal.
- **Borrado**: `ISucursalReadPort`, `SucursalReadPortAdapter` (incl. el
  batch de #599) y su registro DI. El módulo queda sin dependencias fuera
  de su esquema.
- **Migración de renombrado**: `RenameTable`×5 + renombres de
  índices/constraints + en `dim1`: drop `sucursal_id`+su UNIQUE, rename
  `clave_ceco`→`clave`, add `nombre`. Tablas vacías en prod/CI (#603 no
  mergeó); **gate previo: runbook de limpieza de millet_dev** (siembra
  local nunca-mergeada + sus 2 registros de `__EFMigrationsHistory` + las
  5 sucursales CKL–PIN en compartido).
- **Permisos**: `equipos.leer-todos` → `dim3.leer-todos` vía `UpdateData`
  del `codigo` (GUID `0000000c-0003-…` intacto — las asignaciones de rol
  sobreviven) + corrección de las descripciones con vocabulario viejo. Los
  otros 3 códigos no cambian (revisados los 4: solo ese carga vocabulario
  del modelo anterior).
- **ADR-0049**: actualizar SOLO la sección "Primera implementación" (rutas
  de archivos renombradas) — el cuerpo normativo es agnóstico a nombres.
- **Gate del PR**: `grep -rn "SucursalCeCo|SucursalCentroCosto|Subgrupo" backend/src/CentrosCosto` = 0 matches.
- **DoD:** build + los 6 tests del módulo verdes con los nombres nuevos;
  deploy dev verde.

## CECO-PR5 — Siembra single-schema (S) · `centros-costo/pr5-siembra`

- UNA migración en CentrosCosto: `grupos_dim2` (6) → `grupos_dim3` (44) →
  `dim1` (5: clave 101–105 + nombre del Excel) → `dim2` (57, typo `20PDPR`
  normalizado a "SERVICIOS PERIFERICOS") → `dim3` (361). `ON CONFLICT DO
  NOTHING` **sin target** (cubre PK y claves — con target por clave, una
  clave editada post-seed haría 23505 en la re-corrida) + **guard final**
  de conteos (5/6/44/57/361) como mecanismo de ruido. GUIDs congelados
  `0000000c-{bloque}-…-{seq}`.
- Literales generados desde el Excel por script one-off (scratchpad, no se
  mergea); encabezado con fuente/fecha/transformaciones. SQL como constante
  pública (el test de segunda corrida ejecuta EXACTAMENTE ese SQL).
- Ya NO existen: migración de Compartido, orden entre contextos, guard de
  vínculos=5, pre-flight de prod — la siembra no sale del esquema.
- **DoD:** millet_dev y BD limpia de CI muestran el árbol real; segunda
  corrida = no-op PROBADA; cero huérfanos.

## CECO-PR6 — Alcance congelado en máquinas (M/L) · `centros-costo/pr6-alcance` — SIN MOLDE

- Tabla de asignación: `(usuario_id, dim3_id)` UNIQUE — **una columna de
  alcance**, sin nodo polimórfico, sin CHECKs de combinaciones.
- `MarcarAlcanceCommand`: recibe el nodo marcado (dim1 |
  grupo-dim2-bajo-dim1 | dim2 | grupo-dim3-bajo-dim2 | dim3) y **expande a
  las Dim3 vivas** bajo él — inserta/borra hojas; la regla se usa para
  calcular y se tira. **Sin re-evaluación en vivo** (decisión cerrada: una
  máquina nueva NO entra sola al alcance de nadie).
- Query del **árbol de asignación (5 niveles)** por usuario: Dim1 →
  GrupoDim2 (acotado al padre) → Dim2 → GrupoDim3 (acotado) → Dim3, con
  **tri-estado calculado** desde las hojas (ninguno/parcial/todo — nunca
  almacenado) + barra resumen por dimensión. Consecuencia aceptada: quitar
  a propósito y faltar por desactualización pintan igual ("parcial").
- `BuscarDim3` (el selector) gana el filtro de alcance; bypass
  `centros_costo.dim3.leer-todos`.
- **DoD:** marcar un grupo bajo una Dim1 asigna exactamente sus hojas;
  quitar una hoja pinta "parcial" hacia arriba; máquina nueva NO aparece
  para el usuario hasta re-marcar (test explícito); usuario sin filas =
  selector vacío.

## Fase E — Consumo del CC-Máquina (modelo ADR-0050)

> **Reescrito** (antes: `CECO-PR7` Almacén / `CECO-PR8` Compras — 2 PRs por
> módulo con el read-port **validando** en captura, "GUID basura 422", alcance
> del capturista). Ese modelo quedó obsoleto: el diseño cerrado vive en
> **ADR-0050** + [`08-consumo-compras.md`](08-consumo-compras.md). Claves que
> corrigen el modelo viejo:
>
> - El read-port es **solo display** — batch, **sin filtro de alcance**,
>   incluye **inactivas** (ADR-0049). No valida.
> - Compras **no valida existencia** de Dim3 (dependency-free, triada). La
>   validez la garantiza el **picker filtrado** en captura, **no un 422**.
> - Alcance en captura es por semántica: **heredado** (sin alcance) /
>   **elegido-filtrado** (RQ) / **elegido-abierto-por-proxy** (vale, línea
>   manual de OC).
>
> Consumo en **5 PRs por documento** (no 2 por módulo):

- **Fase E PR1 — Toolkit de consumo** · ✅ #687 · `centros-costo/fase-e-pr1-toolkit`.
  `IDim3ReadPort` (batch, sin filtro, incluye inactivas) + query del selector
  abierto + `Dim3Picker` + `useCcMaquinaLabel`. Infra; no cablea documentos.
- **Fase E PR2 — RQ** · ✅ #692 (+ fix visual #697) · `centros-costo/pr2-rq-picker`.
  Picker **filtrado** + display (read-port) + prellenado si el alcance es
  exactamente 1. **Campo OPCIONAL.**
  - **PR2.1** · `centros-costo/pr2-1-obligatorio-rq` — flip a **obligatorio**
    (schema `idLike` + `RuleFor(CentroCostoId).NotNull()` en los 2 validators,
    código `LINEA_RQ_CENTRO_COSTO_REQUERIDO` → 400). Activado **sin** esperar
    sembrado masivo: la config de alcance se hace por demanda en
    `/centros-costo/asignaciones` cuando un capturista topa con picker vacío.
    Sin sub-PR desprendido.
- **Fase E PR3 — OC** · `centros-costo/pr3-oc-picker`. Columna nueva
  `orden_compra_lineas.centro_costo_id` + propagación desde RQ (heredado, **se
  bloquea** — Opción 1 F1, guarda `LINEA_OC_CC_HEREDADO_INMUTABLE`) + línea
  manual (elegido-abierto, proxy comprador `compras.ordenes.crear-sin-rq`, endpoint
  gateado `/api/v1/compras/ordenes/dim3/buscar`). **Campo OPCIONAL.**
  - **PR3.1** · `centros-costo/pr3-1-obligatorio-oc` — flip a **obligatorio** de
    la línea **manual** (schema FE condicional + validator de
    `AgregarLineaManualOcCommand`, código
    `LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO` → 400). Activado **sin** esperar
    sembrado masivo (mismo modelo que PR2.1); el picker de OC es abierto, así
    que el alcance no bloquea al comprador. La heredada no se valida aquí (toma
    lo que traiga la RQ, que ya lo obliga desde PR2.1, y aquí es read-only).
    En el PATCH la regla vive en el dominio como invariante de **post-estado**
    para no romper la semántica parcial (`null` = no tocar). **Cierra el último
    flip de Fase E.**
- **Fase E PR4 — Entrada** · hereda de la línea de OC vía `oc_linea_id` (fallback
  por artículo). Solo lectura.
- **Fase E PR5 — Salida** · `centros-costo/pr5-salida-vale-cc`. Con RQ hereda
  (vía `LineaRqId`, autoritativo en backend, bloqueado read-only); por **vale**
  el almacenista elige (abierto, proxy `almacen.salidas.por-vale`, endpoint
  `/api/v1/almacen/salidas/dim3/buscar`). **Campo OPCIONAL, sin PR5.1** (la
  salida no tiene flip: heredada no captura, vale siempre elige explícito).
  **Muere `MaquinaDestinoId`** de cabecera (`DROP COLUMN`, 0 datos). El display
  usa el patrón puerto+bridge (Almacén no referencia CentrosCosto — ciclo vía
  Compartido). **Cierra la cadena de consumo de Fase E** — con PR2.1 y PR3.1
  activados, no quedan sub-PRs desprendidos de Fase E.
- **DoD por PR:** el documento con máquina válida (elegida del picker o heredada)
  muestra **"clave — nombre"**; id histórico irresoluble → **"No catalogado"**;
  captura por proxy con selector **sin filtro**. **No** hay 422 por "GUID basura"
  — el picker solo ofrece Dim3 reales.

## Fase F — Reportes: SIN PRs (pendiente)

Registrada en [`02-plan-implementacion.md`](02-plan-implementacion.md)
§3-F con sus 3 preguntas abiertas (dueño del reporte, alcance vs
`leer-todos`, qué mide — intención vs gasto real). No se cuela en la
Fase E.

---

## Resumen de granularidad

| PR | Tamaño | Gate |
|---|---|---|
| PR1–PR3 (Fase A) | ✅ mergeados | — |
| PR4 refactor Dim | M | runbook de limpieza de millet_dev previo; grep de vocabulario viejo = 0 |
| PR5 siembra | S | — (CC-G1 y el pre-flight murieron con la separación) |
| PR6 alcance | M/L | sin molde — el diseño §7 es el contrato |
| Fase E — consumo | — | reescrita a 5 PRs (ADR-0050); ver sección "Fase E" arriba |
| — PR1 toolkit | ✅ #687 | — |
| — PR2 RQ (+PR2.1 flip) | S | STOP #2 (re-layout de captura); PR2.1 tras sembrar alcance |
| — PR3 OC · PR4 entrada · PR5 salida | M/S | STOP de coordinación (Compras/Almacén) |

Ruta crítica: PR4→PR5; tras PR5, PR6 y el FE de Fase C son paralelizables; la
Fase E tras PR6. Dentro de E, cadena de herencia PR2→PR3→PR4; PR5 solo depende
de PR1.

**Moldes por pieza:** table-per-level y árbol lazy como antes
(Almacén/ADR-0047); selector = `ArticuloSelector`; CRUD endpoints =
`AlmacenCatalogoEndpoints` con semántica If-Match de Cajas (implementado en
#594). El alcance de PR6 **no tiene molde** — el diseño §7 manda.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión (+ ajustes de PR2/PR3/PR4 durante la Fase A). |
| 0.2 | 2026-07-16 | Modelo Dim: PR4 = refactor, PR5 = siembra single-schema, PR6 = alcance rediseñado (congelado en máquinas), PR7/PR8 = consumo; #603 cerrado; Fase F registrada. |
| 0.3 | 2026-07-18 | Sección de consumo **REESCRITA** al modelo ADR-0050: `CECO-PR7/PR8` (2 PRs por módulo, read-port **validando** en captura + "GUID basura 422" + alcance del capturista) → **Fase E en 5 PRs por documento** (toolkit ✅ #687 / RQ / OC / entrada / salida) + **PR2.1** desprendido (flip a obligatorio). El read-port es solo display (sin filtro, incluye inactivas); Compras no valida existencia; la validez la da el picker filtrado. Divergencia detectada en F3 de PR2. |
| 0.4 | 2026-07-20 | PR2 ✅ #692 (+ fix visual #697). **PR3 (OC)** implementado y **splitteado**: PR3 entrega picker abierto + display + guarda de inmutabilidad de la heredada (Opción 1 F1: hereda **y se bloquea**), campo **OPCIONAL**; el flip a obligatorio de la línea manual se desprende a **PR3.1** (molde de PR2.1). |
| 0.5 | 2026-07-21 | **PR3.1** activado: CC-Máquina obligatorio en la línea **manual** de OC (`LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO`). Dos desvíos del molde de PR2.1 por la convivencia de línea manual y heredada en OC: la regla del PATCH vive en el dominio como invariante de **post-estado** (el comando es PATCH parcial, `null` = no tocar) y la exigencia del schema FE es **condicional** (el mismo schema resuelve el form de la heredada, donde el campo es read-only). Cierra el último flip de Fase E. |
