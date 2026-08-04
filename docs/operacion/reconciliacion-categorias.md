# Runbook — Reconciliación de categoría de artículo (ADR-0046, serie CategoriaArticulo PR3)

Reconcilia el string libre `compartido.articulos.categoria` con el catálogo
`compartido.categorias_articulo`, poblando el FK
`compartido.articulos.categoria_id`.

Gemelo del runbook de unidad de medida
([`reconciliacion-unidades.md`](reconciliacion-unidades.md)); mismo patrón de
tres tiempos **CONTAR → APLICAR → VERIFICAR**.

- **Aditivo y re-ejecutable.** No vive en la migración (la migración de PR2 solo
  crea la columna `categoria_id` + FK + índice). Se corre como paso de runbook,
  deliberadamente, contra cada ambiente.
- **Idempotente.** El `WHERE categoria_id IS NULL` garantiza que (a) re-correrlo
  no duplica ni cambia FKs ya asignados, y (b) **nunca pisa una asignación
  manual** posterior (p. ej. una categoría reasignada desde la UI de artículo).
- **No borra el string legacy `categoria`** (salvo `'Test'`, ver más abajo). Se
  conserva de respaldo: cuando el FK se puebla, `categoria` = nombre de la
  categoría (ya viene sincronizado por `AsignarCategoria` para altas nuevas; aquí
  lo dejamos tal cual, el FK es la fuente de verdad).
- **Match contra el SEED únicamente** (`created_by='seed'`, las 14 reales), NO
  contra las filas de test que dejan las suites de integración en dev/QA.

## Reglas de mapeo (decididas en Fase 1/3)

Normalización antes de matchear: `regexp_replace(btrim(categoria), '\s+', ' ', 'g')`
— recorta bordes y colapsa espacios internos dobles (cubre `INSUMOS  PRODUCCION`
→ `INSUMOS PRODUCCION`). **Sin `lower()`**: los nombres del seed conservan su
capitalización original (`INSUMOS PRODUCCION` vs `Equipo eléctrico`) y el string
legacy del artículo es exactamente el mismo valor, así que el match es
exacto-por-caso tras normalizar espacios.

| Regla | Origen (legacy) | Destino |
|---|---|---|
| **Match directo** | cualquiera de las 14 del seed (normalizado) | esa categoría del seed |
| **Fusión 1** | `categoria = 'Limpieza'` | `MAT DE LIMPIEZA` |
| **Fusión 2** | `categoria = 'Papelería'` | `MATER. DE OFICINA` |
| **Descarte** | `categoria = 'Test'` | → `categoria = NULL` (ruido de seed, no es categoría) |

> **Seguridad del match (verificado).** El nombre normalizado es **UNIQUE entre
> las filas `created_by='seed'`** (0 duplicados) y **no colisiona con ninguna
> fila de test** (0 colisiones). Aun así, todos los `UPDATE` filtran
> `c.created_by='seed'` como defensa: si en un ambiente futuro se creara una
> categoría no-seed con el mismo nombre normalizado, el `JOIN` seguiría siendo
> inequívoco. Si el dry-run reporta un match ambiguo (más filas de las
> esperadas), **PARAR y escalar**.

---

## SECCIÓN 1 — DRY-RUN (solo SELECT: la foto en TU ambiente)

Correr **antes de aplicar nada**, con `psql -v ON_ERROR_STOP=1`. Los conteos
varían por ambiente (dev/QA tienen ruido de test; prod es la data real); la
**invariante que debe cumplirse en todos es el Bloque 4 = 0**.

```sql
-- BLOQUE 1 — match directo: cuántos artículos caen en cada categoría del seed.
SELECT c.nombre, count(a.id) AS articulos
FROM compartido.categorias_articulo c
LEFT JOIN compartido.articulos a
  ON a.categoria_id IS NULL
  AND regexp_replace(btrim(a.categoria), '\s+', ' ', 'g') = c.nombre
WHERE c.created_by = 'seed'
GROUP BY c.nombre ORDER BY articulos DESC, c.nombre;

-- BLOQUE 2 — las 2 fusiones (categoria_id NULL).
SELECT categoria, count(*) AS n
FROM compartido.articulos
WHERE categoria_id IS NULL AND categoria IN ('Limpieza', 'Papelería')
GROUP BY categoria ORDER BY categoria;

-- BLOQUE 3 — 'Test' → irán a NULL.
SELECT count(*) AS test_a_null
FROM compartido.articulos
WHERE categoria_id IS NULL AND categoria = 'Test';

-- BLOQUE 4 — NO MATCH (crítico): categorías con texto que no matchean seed,
--            ni son fusión, ni Test. DEBE dar 0 filas.
SELECT a.categoria, count(*) AS n
FROM compartido.articulos a
WHERE a.categoria IS NOT NULL
  AND a.categoria_id IS NULL
  AND regexp_replace(btrim(a.categoria), '\s+', ' ', 'g') NOT IN ('Limpieza', 'Papelería', 'Test')
  AND NOT EXISTS (
    SELECT 1 FROM compartido.categorias_articulo c
    WHERE c.created_by = 'seed'
      AND c.nombre = regexp_replace(btrim(a.categoria), '\s+', ' ', 'g')
  )
GROUP BY a.categoria ORDER BY n DESC;

-- BLOQUE 5 — totales de control (deben cuadrar; ver abajo).
WITH base AS (
  SELECT a.*, regexp_replace(btrim(a.categoria), '\s+', ' ', 'g') AS norm
  FROM compartido.articulos a WHERE a.categoria_id IS NULL
),
seedset AS (SELECT nombre FROM compartido.categorias_articulo WHERE created_by = 'seed')
SELECT
  (SELECT count(*) FROM base)                                                                    AS total_id_null,
  (SELECT count(*) FROM base WHERE categoria IS NULL)                                            AS categoria_vacia,
  (SELECT count(*) FROM base WHERE categoria IS NOT NULL)                                        AS universo_reconciliar,
  (SELECT count(*) FROM base b WHERE b.categoria IS NOT NULL
     AND EXISTS (SELECT 1 FROM seedset s WHERE s.nombre = b.norm))                               AS match_directo,
  (SELECT count(*) FROM base WHERE categoria IN ('Limpieza', 'Papelería'))                       AS fusiones,
  (SELECT count(*) FROM base WHERE categoria = 'Test')                                           AS test,
  (SELECT count(*) FROM base b WHERE b.categoria IS NOT NULL
     AND b.norm NOT IN ('Limpieza', 'Papelería', 'Test')
     AND NOT EXISTS (SELECT 1 FROM seedset s WHERE s.nombre = b.norm))                           AS no_match;
```

**Condiciones a verificar antes de aplicar:**
- **Bloque 4 = 0 filas.** Si da >0 → **PARAR y escalar**: hay una categoría no
  prevista (¿falta una fila en el seed? ¿un valor legacy con typo/variante no
  contemplada?). No aplicar hasta resolver.
- **Bloque 5 cuadra:**
  - `total_id_null = categoria_vacia + universo_reconciliar`
  - `universo_reconciliar = match_directo + fusiones + test + no_match`

  Si no cuadra → PARAR y escalar.

> **Foto de referencia — local `millet_dev` (2026-07-01, con ruido de test):**
> Bloque 1 = 1457 (681 INSUMOS PRODUCCION, 229 PRENDAS SEGURIDAD, 193 MATER. DE
> OFICINA, 92 MEDICINAS Y FARMACIA, 90 MAT DE LIMPIEZA, 89 SUMI. LABORATORIO, 49
> IMPRESIÓN Y DIGITAL, 28 MAT DE TI Y COMUNIC, 1 × 6 singletons) · Bloque 2 = 2
> (Limpieza 1, Papelería 1) · Bloque 3 = 121 · **Bloque 4 = 0** · Bloque 5:
> `1734 = 154 + 1580` y `1580 = 1457 + 2 + 121 + 0`. **Cuadra.** En prod los
> números serán la data real (sin ruido de test); la invariante Bloque 4 = 0 debe
> mantenerse.

---

## SECCIÓN 2 — APLICAR (dentro de una transacción)

Los 4 `UPDATE` van en un `BEGIN … COMMIT` con un **GUARD**: un bloque `DO` que
captura las filas afectadas de cada `UPDATE` (`GET DIAGNOSTICS`) y **aborta la
transacción** (⇒ `ROLLBACK`, no persiste) si algún conteo difiere del esperado o
si quedan `pendientes ≠ 0`. **El `COMMIT` solo persiste si todo cuadra.** Correr
con `psql -v ON_ERROR_STOP=1`.

> ⚠️ **Ajustar por ambiente.** Los esperados `1457 / 1 / 1 / 121` del `IF` del
> guard son los de `millet_dev`. Antes de correr en QA/prod, **reemplazarlos por
> los conteos del dry-run de ESE ambiente** (Bloque 1 total · Bloque 2 Limpieza ·
> Bloque 2 Papelería · Bloque 3 Test). Si no se ajustan, el guard aborta — que es
> justo lo que queremos: no correr a ciegas. `pendientes` siempre debe ser 0.

```sql
BEGIN;

-- PASO 0 OBLIGATORIO (todos los ambientes, incluido local): respaldo de las
-- filas afectadas ANTES de tocar nada. Habilita la reversa TOTAL, incluido el
-- paso 4 (Test → NULL), que es destructivo del legacy. (DDL transaccional en
-- Postgres: si la tx hace ROLLBACK, la tabla se deshace; si COMMIT, persiste.)
DROP TABLE IF EXISTS _bak_articulos_categoria;
CREATE TABLE _bak_articulos_categoria AS
  SELECT id, categoria, categoria_id
  FROM compartido.articulos
  WHERE categoria IS NOT NULL;   -- las filas candidatas a cambiar

-- UPDATES 1–4 con GUARD. Aborta (RAISE EXCEPTION → ROLLBACK) si algún conteo
-- != esperado o pendientes != 0; el COMMIT de abajo solo corre si el DO pasó.
DO $$
DECLARE n1 int; n2 int; n3 int; n4 int; pend int;
BEGIN
  -- UPDATE 1 — match directo contra las 14 del seed. created_by='seed' hace el
  -- match inequívoco (nombre normalizado UNIQUE entre seed, sin colisión test).
  UPDATE compartido.articulos a
  SET categoria_id = c.id
  FROM compartido.categorias_articulo c
  WHERE c.created_by = 'seed'
    AND a.categoria_id IS NULL
    AND regexp_replace(btrim(a.categoria), '\s+', ' ', 'g') = c.nombre;
  GET DIAGNOSTICS n1 = ROW_COUNT;

  -- UPDATE 2 — fusión 'Limpieza' → MAT DE LIMPIEZA.
  UPDATE compartido.articulos a
  SET categoria_id = c.id
  FROM compartido.categorias_articulo c
  WHERE c.created_by = 'seed' AND c.nombre = 'MAT DE LIMPIEZA'
    AND a.categoria_id IS NULL AND a.categoria = 'Limpieza';
  GET DIAGNOSTICS n2 = ROW_COUNT;

  -- UPDATE 3 — fusión 'Papelería' → MATER. DE OFICINA.
  UPDATE compartido.articulos a
  SET categoria_id = c.id
  FROM compartido.categorias_articulo c
  WHERE c.created_by = 'seed' AND c.nombre = 'MATER. DE OFICINA'
    AND a.categoria_id IS NULL AND a.categoria = 'Papelería';
  GET DIAGNOSTICS n3 = ROW_COUNT;

  -- UPDATE 4 — 'Test' → limpiar el legacy (categoria_id ya es NULL). Deja esas
  -- filas completamente sin categoría (indistinguibles de las vacías).
  UPDATE compartido.articulos
  SET categoria = NULL
  WHERE categoria = 'Test' AND categoria_id IS NULL;
  GET DIAGNOSTICS n4 = ROW_COUNT;

  -- Verificación maestra: nada con texto puede quedar sin FK.
  SELECT count(*) INTO pend
  FROM compartido.articulos
  WHERE categoria IS NOT NULL AND categoria_id IS NULL;

  RAISE NOTICE 'UPDATE 1=% | UPDATE 2=% | UPDATE 3=% | UPDATE 4=% | pendientes=%',
    n1, n2, n3, n4, pend;

  -- ⚠️ AJUSTAR 1457/1/1/121 a los conteos del dry-run de ESTE ambiente.
  IF n1 <> 1457 OR n2 <> 1 OR n3 <> 1 OR n4 <> 121 OR pend <> 0 THEN
    RAISE EXCEPTION
      'GUARD: conteo(s) != esperado o pendientes != 0 -> abortando (ROLLBACK, no persiste)';
  END IF;
END $$;

-- Confirmación visible fuera del DO (misma tx). DEBE ser 0.
SELECT count(*) AS pendientes
FROM compartido.articulos
WHERE categoria IS NOT NULL AND categoria_id IS NULL;

COMMIT;   -- solo corre si el DO no abortó
```

> **Orden e independencia.** UPDATE 1 no toca `Limpieza`/`Papelería`/`Test` (no
> son nombres del seed), así que quedan con `categoria_id` NULL para que 2/3/4
> los procesen. Cada `UPDATE` es idempotente por su `WHERE categoria_id IS NULL`
> (el 4 por `categoria='Test'`).

---

## SECCIÓN 3 — VERIFICAR (post-aplicación)

```sql
-- Query maestra: NADA con texto puede quedar sin FK. DEBE ser 0.
SELECT count(*) AS pendientes
FROM compartido.articulos
WHERE categoria IS NOT NULL AND categoria_id IS NULL;

-- Distribución final por categoría (comparar contra el Bloque 1 del dry-run;
-- MAT DE LIMPIEZA = directo + fusión Limpieza; MATER. DE OFICINA = directo +
-- fusión Papelería).
SELECT c.nombre, count(a.id) AS articulos
FROM compartido.categorias_articulo c
JOIN compartido.articulos a ON a.categoria_id = c.id
WHERE c.created_by = 'seed'
GROUP BY c.nombre ORDER BY articulos DESC, c.nombre;

-- Cobertura global.
SELECT
  count(*)                                          AS total,
  count(*) FILTER (WHERE categoria_id IS NOT NULL)  AS con_fk,
  count(*) FILTER (WHERE categoria_id IS NULL)      AS sin_fk
FROM compartido.articulos;
```

**Si la query maestra NO da 0:**
- Si estás **dentro de la transacción** (aún no hiciste COMMIT) → `ROLLBACK` y
  vuelve al dry-run: algo cambió entre CONTAR y APLICAR (¿alguien insertó
  artículos con una categoría nueva?). Re-evaluar el Bloque 4.
- Si ya hiciste **COMMIT** → escalar; usar la reversa (abajo) o restaurar del
  respaldo `_bak_articulos_categoria`.

---

## SECCIÓN 4 — NOTAS DE EJECUCIÓN

- **Por ambiente, en orden dev → QA → prod.** Cada ambiente corre su **propio**
  dry-run (Sección 1) primero — los conteos varían; la invariante que debe
  cumplirse en todos es **Bloque 4 = 0**. El deploy (GitHub Actions) solo aplica
  el **esquema** (`ef database update`), **no** esta reconciliación.
- **Privilegio mínimo:** `UPDATE` en `compartido.articulos` + `SELECT` en
  `compartido.categorias_articulo` + `CREATE`/`DROP` de la tabla de respaldo
  `_bak_articulos_categoria` (scratch, no es dato de la app). **En prod lo
  ejecuta Eduardo** (dueño del ambiente). Nunca `DELETE`/`TRUNCATE`/`ALTER` sobre
  `compartido.articulos`; los únicos *writes* sobre datos de la app son los 4
  `UPDATE`.
- **El legacy `categoria` NO se borra** (salvo `'Test'`) — queda de respaldo. Los
  ~1,459 reconciliados conservan su string = nombre de la categoría; el FK es la
  fuente de verdad.
- **Idempotente:** re-correr el bloque APLICAR con `WHERE categoria_id IS NULL`
  reporta `UPDATE 0` en 1/2/3 y no pisa asignaciones manuales. El 4 re-corrido
  también es 0 (ya no quedan `categoria='Test'` con id NULL).
- **Reversa (post-COMMIT).** El respaldo obligatorio `_bak_articulos_categoria`
  (creado en el PASO 0 del bloque APLICAR) permite reversa **total**, incluido el
  paso 4 (Test → NULL):
  `UPDATE compartido.articulos a SET categoria = b.categoria, categoria_id = b.categoria_id FROM _bak_articulos_categoria b WHERE a.id = b.id;`
  Verificada la reversa, se puede `DROP TABLE _bak_articulos_categoria`.
- **Residuo de test en dev/QA:** las suites de integración dejan filas en
  `categorias_articulo` (`created_by <> 'seed'`) y artículos de prueba. El
  `created_by='seed'` de los `UPDATE` y el `WHERE categoria_id IS NULL` los
  ignoran; no afectan la reconciliación de la data real. (La pantalla de
  categorías se ve "inflada" en dev/QA por esas filas de test — es esperado.)

## Registro histórico

| Ambiente | Estado | Detalle |
|---|---|---|
| Local `millet_dev` | ✅ 2026-07-01 | `UPDATE 1457/1/1/121`; cobertura `con_fk/sin_fk` **3/1734 → 1462/275**; verificación maestra = 0. Backup `_bak_articulos_categoria` (1583 filas). Corrido en tx con guard de conteos (aborta si difieren) + COMMIT. El +1 de INSUMOS PRODUCCION (682) es 1 artículo real ya reconciliado que la idempotencia respetó. |
| **QA** | ⛔ pendiente | correr tras promover PR3, con su propio dry-run |
| **Prod** | ⛔ pendiente | lo ejecuta Eduardo, tras la carga SAP y con respaldo `_bak_articulos_categoria` |

## Causa raíz / fix definitivo (pendiente)

Este runbook existe porque los artículos preexistentes nacieron con `categoria`
como texto libre (antes del catálogo). Como en unidad de medida, el **fix de
raíz** es que la **carga SAP resuelva el FK en el `INSERT`** (JOIN a
`categorias_articulo` por nombre normalizado con la misma regla de este runbook),
para que ningún artículo cargado nazca en NULL. Mientras eso no exista, cada
ambiente corre esta reconciliación **una vez** tras cargar artículos.
