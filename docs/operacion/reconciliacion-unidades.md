# Runbook — Reconciliación de unidad de medida (ADR-0046 Etapa 1b)

Reconcilia el string libre `compartido.articulos.unidad_medida_default` con el
catálogo `compartido.unidades_medida`, poblando el FK
`compartido.articulos.unidad_medida_id`.

- **Aditivo y re-ejecutable.** No vive en la migración (la migración solo crea
  la columna + FK + índice). Se corre como paso de runbook, deliberadamente,
  contra cada ambiente.
- **Idempotente.** El `WHERE unidad_medida_id IS NULL` garantiza que (a)
  re-correrlo no duplica ni cambia FKs ya asignados, y (b) **nunca pisa una
  asignación manual** posterior. Re-corrible tras agregar unidades nuevas o
  tras resolver a mano los pendientes (PPZA/PSP/empaque).
- **No toca las líneas.** Solo hace `UPDATE` sobre `compartido.articulos`; los
  snapshots `unidad_medida` de `requisicion_lineas` / `orden_compra_lineas` /
  `lineas_movimiento` / `lineas_devolucion_proveedor` son históricos y quedan
  intactos.
- **No toca `unidad_medida_default`.** El string legacy se conserva (los
  artículos no mapeados siguen operando con él; ver Etapa 3).

## Mapa limpio

Normalización antes de matchear: `upper(btrim(regexp_replace(valor, '\.$', '')))`
— mayúsculas, sin espacios, sin punto final (cubre `PZA.` y `pza`).

| Legacy (normalizado) | Código catálogo |
|---|---|
| `PZA`, `PIEZA` | `PZA` |
| `L`, `LITRO` | `L` |
| `KG` | `KG` |
| `METRO` | `M` |
| `ML` | `ML` |
| `HR` | `HR` |
| `PAR` | `PAR` |

**No se auto-mapean** (quedan FK NULL + legacy intacto, para revisión / Etapa 3):
`PPZA`, `PSP` (revisión manual), y el empaque variable `CAJA`, `PAQ`,
`PAQUETE`, `PQT`, `ROLLO`, `TAMBO`, `CUBETA`, `GALON`, `KIT`, `SET`, `BLOCK`
(equivalencia dependiente del producto → la resuelve la Etapa 3).

## Script 1 — Reconciliación (idempotente)

> Mantener en sync con el test `ReconciliacionUnidadesTests` (mismo SQL).

```sql
WITH mapa(legacy_norm, codigo) AS (
    VALUES
        ('PZA', 'PZA'), ('PIEZA', 'PZA'),
        ('L', 'L'), ('LITRO', 'L'),
        ('KG', 'KG'),
        ('METRO', 'M'),
        ('ML', 'ML'),
        ('HR', 'HR'),
        ('PAR', 'PAR')
)
UPDATE compartido.articulos a
SET unidad_medida_id = u.id
FROM mapa m
JOIN compartido.unidades_medida u ON u.codigo = m.codigo
WHERE a.unidad_medida_id IS NULL
  AND upper(btrim(regexp_replace(a.unidad_medida_default, '\.$', ''))) = m.legacy_norm;
```

Para previsualizar sin aplicar, envolver en una transacción y hacer `ROLLBACK`,
o cambiar el `UPDATE` por un `SELECT` con el mismo `JOIN`/`WHERE`.

## Script 2 — Pendientes de revisión

Lista los artículos que quedaron sin FK (no mapeados), agrupados por el valor
legacy normalizado. Señala `PPZA`/`PSP` (revisión manual) y el empaque
variable (Etapa 3). **No es un mapeo silencioso** — es el insumo para decidir.

```sql
SELECT
    upper(btrim(regexp_replace(unidad_medida_default, '\.$', ''))) AS legacy_normalizado,
    count(*)                                                       AS articulos,
    string_agg(DISTINCT unidad_medida_default, ', ' ORDER BY unidad_medida_default) AS variantes
FROM compartido.articulos
WHERE unidad_medida_id IS NULL
GROUP BY 1
ORDER BY articulos DESC;
```

## Verificación post-corrida

```sql
-- Cobertura: cuántos artículos quedaron con FK vs sin FK.
SELECT
    count(*)                                  AS total,
    count(*) FILTER (WHERE unidad_medida_id IS NOT NULL) AS con_fk,
    count(*) FILTER (WHERE unidad_medida_id IS NULL)     AS sin_fk_legacy
FROM compartido.articulos;
```

## Promoción a otros ambientes — procedimiento de dos fases

> **Por qué existe este paso.** La reconciliación es un runbook **manual por-ambiente**.
> El deploy (GitHub Actions) solo aplica el **esquema** (`ef database update`); **no**
> corre ni la reconciliación ni la carga SAP. Sin reconciliar, los artículos quedan con
> `unidad_medida_id` NULL → el **guard de decimales (Etapa 2)** hace *skip* → deja
> capturar decimales inválidos (ej. unidad "PZA" acepta `1.5`). **No es bug: es dato.**
> Por eso **cada ambiente nuevo** (incluido prod) requiere correr esto **una vez** tras
> cargar artículos.

### Precondiciones
- El esquema de la **Etapa 1b** ya está aplicado (columna `unidad_medida_id` + FK).
- Los artículos SAP ya están cargados (si no, este paso va **después** de la carga).
- Quien ejecuta tiene **mínimo privilegio efectivo**: `UPDATE` en `compartido.articulos`
  + `SELECT` en `compartido.unidades_medida`. **Nada más.**
  - **En prod lo ejecuta Eduardo** (dueño del ambiente). **No** se usa un rol con
    `ALL PRIVILEGES` (eso fue un atajo temporal en dev) — solo el privilegio mínimo.
- Nunca `DELETE`/`TRUNCATE`/`ALTER`. El único *write* es el `UPDATE` del Script 1 sobre
  `compartido.articulos`.

### Fase 1 — dry-run (NO persiste): verificar antes de tocar nada
Todo dentro de una transacción que termina en **`ROLLBACK`** — muestra *exactamente* qué
haría el `UPDATE` sin escribir. Correr con `psql -v ON_ERROR_STOP=1`.

```sql
-- Cobertura ANTES (esperado en un ambiente sin reconciliar: con_fk≈0).
SELECT count(*) FILTER (WHERE unidad_medida_id IS NOT NULL) AS con_fk,
       count(*) FILTER (WHERE unidad_medida_id IS NULL)     AS sin_fk
FROM compartido.articulos;

BEGIN;

-- Script 1 (idéntico al de la sección "Script 1" de arriba).
WITH mapa(legacy_norm, codigo) AS (
    VALUES
        ('PZA', 'PZA'), ('PIEZA', 'PZA'),
        ('L', 'L'), ('LITRO', 'L'),
        ('KG', 'KG'),
        ('METRO', 'M'),
        ('ML', 'ML'),
        ('HR', 'HR'),
        ('PAR', 'PAR')
)
UPDATE compartido.articulos a
SET unidad_medida_id = u.id
FROM mapa m
JOIN compartido.unidades_medida u ON u.codigo = m.codigo
WHERE a.unidad_medida_id IS NULL
  AND upper(btrim(regexp_replace(a.unidad_medida_default, '\.$', ''))) = m.legacy_norm;

-- Cobertura DESPUÉS (dentro de la tx).
SELECT count(*) FILTER (WHERE unidad_medida_id IS NOT NULL) AS con_fk,
       count(*) FILTER (WHERE unidad_medida_id IS NULL)     AS sin_fk
FROM compartido.articulos;

-- Pendientes (Script 2).
SELECT upper(btrim(regexp_replace(unidad_medida_default, '\.$', ''))) AS legacy_normalizado,
       count(*) AS articulos
FROM compartido.articulos
WHERE unidad_medida_id IS NULL
GROUP BY 1 ORDER BY articulos DESC;

ROLLBACK;
```

**Verificar las 4 condiciones antes de continuar:**
- **(a)** el `UPDATE` afectó el N esperado de filas (códigos limpios PZA/L/KG/M/ML/HR/PAR; en prod, varios miles).
- **(b)** `con_fk` subió y `sin_fk` bajó a **solo** empaque variable + PPZA/PSP.
- **(c)** **ningún código limpio** (PZA/L/KG/M/ML/HR/PAR) quedó en los pendientes.
- **(d)** solo se tocó `compartido.articulos` (por construcción del `UPDATE`).

Si algo no cuadra (un código limpio sin mapear, números fuera de rango) → **no commitees**;
investiga (¿falta el seed de una unidad en `unidades_medida`? ¿el string legacy trae una
variante no contemplada en el mapa?).

### Fase 2 — corrida real (solo si las 4 condiciones cuadran)
```sql
BEGIN;
-- ...Script 1 idéntico al de la Fase 1...
COMMIT;
```

### Verificación post (durabilidad + UI)
- Cobertura en una **conexión fresca** (no la misma sesión del commit) → confirma persistido.
- Prueba en la UI: un artículo con unidad **"PZA"** y cantidad **`1.5`** en una RQ/OC →
  debe **rechazar** con `422 CANTIDAD_DECIMALES_EXCEDE_UNIDAD`.

### Registro histórico
| Ambiente | Estado | Detalle |
|---|---|---|
| Local `millet_dev` | ✅ | ya reconciliado (≈1478 con FK / 133 NULL) |
| **Azure dev** (`pg-millet-dev-mxc-01`, db `postgres`) | ✅ 2026-06-23 | `UPDATE 1347`; cobertura **0/1462 → 1347/115**; pendientes = empaque variable (113) + PPZA(1)/PSP(1). Idempotente (re-run = `UPDATE 0`). Dos fases (dry-run+ROLLBACK → COMMIT). |
| **Prod** | ⛔ **pendiente** | correr al promover, tras la carga SAP. |

### Causa raíz / fix definitivo (pendiente)
Este runbook existe porque **la carga SAP inserta con `unidad_medida_id` NULL**
(`docs/operacion/carga-sap-scripts/02_load_articulos_local.sql` hace `INSERT` directo sin
el FK), así que hay que reconciliar **después** en cada ambiente. **Fix de raíz pendiente:**
que la carga SAP **resuelva el FK en el `INSERT`** (JOIN a `unidades_medida` por código,
con la misma normalización del mapa). Así ningún artículo nace en NULL y este runbook deja
de ser necesario para los datos cargados por SAP (seguiría aplicando solo a artículos
legacy preexistentes, de haberlos).

## Notas

- En `dev` el universo es ~1.5k artículos; en `prod`, ~13k (ADR-0044). El
  `UPDATE` es una sola pasada, rápido.
- Manual resoluble: para asignar a mano un artículo de empaque a su unidad base
  + factor, eso es **Etapa 3** (no este runbook). Si se asigna el FK a mano
  aquí, este script lo respeta (no lo pisa).
- La validación de decimales por unidad (**Etapa 2**) ya está en `main` para
  **Compras** (PR-2a): por eso reconciliar **habilita** el bloqueo de decimales
  inválidos en ese ambiente. Almacén (2b) y frontend (2c) quedan pendientes.
