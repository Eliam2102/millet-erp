# Runbook — Carga única SAP → millet_erp (proveedores + artículos) — PRODUCCIÓN

> **Procedimiento de una sola vez, documentado y repetible** (no es software desplegado).
> Flujo: extracción SAP → CSV → staging → vista de transformación → **dry-run (STOP)** → **INSERT transaccional autoverificable** → verificación.
> **Validado** con datos reales en `local millet_dev` y `Azure dev` (proveedores 2925, artículos Fase 1 1452). Este runbook describe la **variante de PRODUCCIÓN**, que difiere de esas pruebas en dos puntos marcados con 🅿️.

---

## 0. Diferencias PRUEBA → PRODUCCIÓN (lo único que cambia)

| Aspecto | Prueba (local / Azure dev) | 🅿️ Producción |
|---|---|---|
| Clave de proveedor | `P` + consecutivo (`P000001…`) | `{prefijo}-{NNNNNN}` por grupo (NAC/EXT), continuando desde el máximo existente |
| Universo de artículos | Solo grupos papelería/limpieza/insumos (`ItmsGrpCod IN (...)`) → 1452 | **Todos** los materiales no-cancelados (sin filtro de grupo) → ~12,984 |

Todo lo demás (filtros base, derivaciones, dedup, guards, mecanismo de carga) es idéntico a lo ya validado.

---

## 1. Prerrequisitos — confirmar ANTES de ejecutar

### 1a. 🛑 Sign-off de Eduardo (decisiones de dominio)
No ejecutar sin confirmar estas cuatro:
1. **Proveedores — clave**: `{prefijo}-{NNNNNN}` por grupo (NAC/EXT).
2. **Proveedores — dedup**: por `clave_legacy = CardCode` (no por RFC; los datos mostraron RFC como mala llave: `XEXX` compartido + RFC reales repetidos).
3. **Artículos — activos fijos**: excluir `ItemType='F'` (pertenecen al módulo Activos Fijos, no al catálogo de materiales).
4. **Artículos — filas incompletas**: excluir las sin nombre/uom → van a una **lista de revisión** para corregir en SAP y re-cargar después (no entran a esta carga).

### 1b. Accesos
- **Postgres prod**: rol con `SELECT`+`INSERT` en `compartido.proveedores`, `compartido.articulos`, `SELECT` en `compartido.monedas`, owner (o `CREATE`) en un schema `staging_carga`. **Para rollback se requiere `DELETE`** en proveedores/articulos — o que Eduardo (owner) esté disponible para revertir. Host/dbname de prod: _(los define Eduardo)_.
- **SAP read-only**: login para `sqlcmd` (⚠️ ver §6 — rotar el login que se filtró).

### 1c. Operativos
- **Ventana de bajo tráfico** (la carga es masiva).
- **Backup** de `compartido.proveedores` y `compartido.articulos` antes de empezar (o confiar en la transacción + guard).
- **Baseline**: registrar conteos previos de prod.
  ```sql
  SELECT count(*) AS proveedores FROM compartido.proveedores;
  SELECT count(*) AS articulos   FROM compartido.articulos;
  SELECT count(*) AS monedas     FROM compartido.monedas;
  SELECT gen_random_uuid();  -- confirmar que existe en prod
  ```

---

## 2. Extracción desde SAP (read-only)

Working dir **fuera del repo** (p.ej. `C:\Users\Victor\carga_prod\`). **Los CSV traen PII → no versionar.**
Export con el mecanismo validado: `sqlcmd -i <ruta-windows del .sql>` (`cygpath -w` + `MSYS2_ARG_CONV_EXCL='*'`), salida `-u` (UTF-16) → `iconv` a UTF-8. Verificar UTF-8 válido y conteo de filas.

### 2a. Proveedores → `proveedores_sap.csv`
```sql
-- SQL Server / SAP (READ-ONLY). Universo esperado: ~2,925 (NAC + EXT).
SELECT
    T0.CardCode                                AS card_code,
    T1.GroupName                               AS group_name,
    CASE WHEN T1.GroupName LIKE 'PROV. NACIONALES%'  THEN 'NAC'
         WHEN T1.GroupName LIKE 'PROV. EXTRANJEROS%' THEN 'EXT'
         ELSE LEFT(LTRIM(REPLACE(T1.GroupName,'PROV.','')),3) END AS prefijo,
    T0.CardName                                AS razon_social,
    NULLIF(LTRIM(RTRIM(T0.CardFName)),'')      AS nombre_comercial,
    NULLIF(LTRIM(RTRIM(T0.LicTradNum)),'')     AS rfc,
    NULLIF(LTRIM(RTRIM(T0.E_Mail)),'')         AS email,
    NULLIF(LTRIM(RTRIM(T0.Phone1)),'')         AS telefono,
    T0.Currency                                AS currency,
    ISNULL(T2.ExtraDays,0) + ISNULL(T2.ExtraMonth,0)*30 AS condiciones_pago_dias
FROM OCRD T0
JOIN OCRG T1 ON T0.GroupCode = T1.GroupCode AND T1.GroupType = 'S'
LEFT JOIN OCTG T2 ON T0.GroupNum = T2.GroupNum
WHERE T0.CardType='S' AND T1.GroupName LIKE 'PROV.%'
  AND T0.validFor='Y' AND T0.frozenFor='N'
ORDER BY T1.GroupName, T0.CardCode;
```

### 2b. Artículos → `articulos_sap.csv`  🅿️ (sin filtro de grupo)
```sql
-- Universo esperado: ~12,984 (12,714 Estándar + 270 Servicio).
SELECT
    T0.ItemCode    AS item_code,
    T0.ItemName    AS nombre,
    T0.InvntryUom  AS unidad_medida,
    T0.InvntItem   AS invnt_item,
    T0.ItmsGrpCod  AS grupo_cod,
    T1.ItmsGrpNam  AS categoria
FROM OITM T0
JOIN OITB T1 ON T0.ItmsGrpCod = T1.ItmsGrpCod
WHERE T0.validFor='Y' AND T0.frozenFor='N'
  AND T0.ItemType='I'                                  -- excluye activos fijos 'F'
  AND NULLIF(LTRIM(RTRIM(T0.ItemName)),'')   IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(T0.InvntryUom)),'') IS NOT NULL
  -- 🅿️ PROD: NO va el filtro ItmsGrpCod IN (...) — todos los materiales.
ORDER BY T0.ItmsGrpCod, T0.ItemCode;
```
> Opcional pre-carga: guardar las **filas incompletas** (sin nombre/uom) en un CSV de revisión para SAP:
> `... WHERE validFor='Y' AND frozenFor='N' AND ItemType='I' AND (ItemName='' OR InvntryUom='')`.

---

## 3. Carga en Postgres (staging → dry-run → INSERT → verificación)

> El rol scoped **no** puede `CREATE SCHEMA` en la BD; `staging_carga` debe existir (owner = el rol). Si no, lo crea Eduardo: `CREATE SCHEMA staging_carga AUTHORIZATION <rol>;`

### 3a. Staging + `\copy`
```sql
DROP TABLE IF EXISTS staging_carga.proveedores_sap CASCADE;
CREATE TABLE staging_carga.proveedores_sap (
  card_code text, group_name text, prefijo text, razon_social text,
  nombre_comercial text, rfc text, email text, telefono text,
  currency text, condiciones_pago_dias int);
\copy staging_carga.proveedores_sap FROM 'proveedores_sap.csv' WITH (FORMAT csv, HEADER true)

DROP TABLE IF EXISTS staging_carga.articulos_sap CASCADE;
CREATE TABLE staging_carga.articulos_sap (
  item_code text, nombre text, unidad_medida text, invnt_item text, grupo_cod int, categoria text);
\copy staging_carga.articulos_sap FROM 'articulos_sap.csv' WITH (FORMAT csv, HEADER true)
```

### 3b. Vistas de transformación

**Proveedores 🅿️ — clave por grupo, continuando desde el máximo existente:**
```sql
CREATE OR REPLACE VIEW staging_carga.v_proveedores_carga AS
WITH existentes AS (   -- máximo consecutivo ya usado por prefijo en prod
  SELECT split_part(clave,'-',1) AS prefijo,
         max(CAST(split_part(clave,'-',2) AS int)) AS max_num
  FROM compartido.proveedores
  WHERE clave ~ '^[A-Z]{3}-[0-9]{6}$'
  GROUP BY 1)
SELECT
    s.prefijo || '-' || lpad(
      (COALESCE(e.max_num,0)
       + row_number() OVER (PARTITION BY s.prefijo ORDER BY s.card_code))::text, 6, '0') AS clave,
    s.card_code                                   AS clave_legacy,
    s.razon_social,
    COALESCE(s.nombre_comercial, s.razon_social)  AS nombre_comercial,
    s.rfc,
    CASE WHEN s.rfc IS NULL OR s.rfc = 'XEXX010101000' THEN 0
         WHEN length(s.rfc) = 13 THEN 1 ELSE 0 END AS tipo_persona,
    0 AS estatus, s.email, s.telefono, s.condiciones_pago_dias,
    m.id AS moneda_preferida_id
FROM staging_carga.proveedores_sap s
LEFT JOIN existentes e ON e.prefijo = s.prefijo
LEFT JOIN compartido.monedas m
    ON m.codigo = CASE WHEN s.currency='MXP' THEN 'MXN'
                       WHEN s.currency='##'  THEN NULL ELSE s.currency END;
```

**Artículos — idéntico a lo validado:**
```sql
CREATE OR REPLACE VIEW staging_carga.v_articulos_carga AS
SELECT TRIM(s.item_code) AS clave, s.nombre,
       s.unidad_medida AS unidad_medida_default,
       CASE WHEN s.invnt_item='N' THEN 1 ELSE 0 END AS naturaleza,
       0 AS estatus, s.categoria
FROM staging_carga.articulos_sap s;
```

### 3c. DRY-RUN — **STOP y revisar** (no inserta)
```sql
-- Proveedores
SELECT
  (SELECT count(*) FROM staging_carga.v_proveedores_carga) AS a_insertar,
  (SELECT count(*) FROM staging_carga.v_proveedores_carga WHERE tipo_persona=1) AS fisicas,
  (SELECT count(*) FROM staging_carga.v_proveedores_carga WHERE moneda_preferida_id IS NULL) AS sin_moneda,
  (SELECT count(*) FROM staging_carga.proveedores_sap s WHERE s.currency<>'##'
     AND NOT EXISTS (SELECT 1 FROM compartido.monedas m WHERE m.codigo=CASE WHEN s.currency='MXP' THEN 'MXN' ELSE s.currency END)) AS moneda_no_catalogo,
  (SELECT count(*) FROM staging_carga.v_proveedores_carga v JOIN compartido.proveedores p ON p.clave=v.clave) AS colision_clave,
  (SELECT count(*) FROM (SELECT clave FROM staging_carga.v_proveedores_carga GROUP BY clave HAVING count(*)>1) d) AS dup_en_lote,
  -- 🅿️ dedup por CardCode: cuántos clave_legacy ya existen en prod
  (SELECT count(*) FROM staging_carga.v_proveedores_carga v JOIN compartido.proveedores p ON p.clave_legacy=v.clave_legacy) AS cardcode_ya_existe;

-- Artículos
SELECT
  (SELECT count(*) FROM staging_carga.v_articulos_carga) AS a_insertar,
  (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE naturaleza=0) AS estandar,
  (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE naturaleza=1) AS servicios,
  (SELECT count(*) FROM staging_carga.v_articulos_carga v JOIN compartido.articulos a ON a.clave=v.clave) AS colision_clave,
  (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE length(categoria)>100) AS categoria_larga,
  (SELECT count(*) FROM (SELECT clave FROM staging_carga.v_articulos_carga GROUP BY clave HAVING count(*)>1) d) AS dup_en_lote;
```
**Criterios para continuar:** `colision_clave=0`, `dup_en_lote=0`, `moneda_no_catalogo=0`, `categoria_larga=0`.
**`cardcode_ya_existe` / colisiones de ItemCode > 0** ⇒ ya hay datos previos de SAP en prod ⇒ **decidir con Eduardo** (omitir existentes con un `WHERE NOT EXISTS`, o limpiar). No insertar a ciegas.

### 3d. INSERT transaccional autoverificable (COMMIT solo si cuadra)
> Reemplazar `2925` y `12984` por los `a_insertar` reales del dry-run aprobado.
```sql
BEGIN;
INSERT INTO compartido.proveedores
    (id, clave, clave_legacy, razon_social, nombre_comercial, rfc, tipo_persona,
     estatus, email, telefono, condiciones_pago_dias, moneda_preferida_id, version, created_at, updated_at)
SELECT gen_random_uuid(), clave, clave_legacy, razon_social, nombre_comercial, rfc, tipo_persona,
       estatus, email, telefono, condiciones_pago_dias, moneda_preferida_id, 1, now(), now()
FROM staging_carga.v_proveedores_carga;

INSERT INTO compartido.articulos
    (id, clave, nombre, unidad_medida_default, naturaleza, estatus, categoria, version, created_at, updated_at)
SELECT gen_random_uuid(), clave, nombre, unidad_medida_default, naturaleza, estatus, categoria, 1, now(), now()
FROM staging_carga.v_articulos_carga;

DO $$
DECLARE p int; a int;
BEGIN
  SELECT count(*) INTO p FROM compartido.proveedores
    WHERE EXISTS (SELECT 1 FROM staging_carga.proveedores_sap s WHERE s.card_code = clave_legacy);
  SELECT count(*) INTO a FROM compartido.articulos a2
    WHERE EXISTS (SELECT 1 FROM staging_carga.articulos_sap s WHERE TRIM(s.item_code)=a2.clave);
  RAISE NOTICE 'intra-txn -> proveedores=%, articulos=%', p, a;
  IF p <> <A_INSERTAR_PROV> OR a <> <A_INSERTAR_ART> THEN
    RAISE EXCEPTION 'ABORT: conteos no cuadran (prov=%, art=%)', p, a;
  END IF;
END $$;
COMMIT;
```

### 3e. Verificación post-commit
```sql
SELECT count(*) AS total_proveedores FROM compartido.proveedores;  -- baseline + a_insertar
SELECT count(*) AS total_articulos   FROM compartido.articulos;    -- baseline + a_insertar
-- spot-check sin PII
SELECT clave, clave_legacy, length(rfc) AS rfc_len, tipo_persona FROM compartido.proveedores
  WHERE EXISTS (SELECT 1 FROM staging_carga.proveedores_sap s WHERE s.card_code=clave_legacy) ORDER BY clave LIMIT 10;
```

---

## 4. Rollback

**Con `DELETE` (preferido):**
```sql
DELETE FROM compartido.proveedores p
 USING staging_carga.proveedores_sap s WHERE p.clave_legacy = s.card_code;   -- por CardCode (seguro)
DELETE FROM compartido.articulos a
 USING staging_carga.articulos_sap s  WHERE a.clave = TRIM(s.item_code);     -- seguro porque colision_clave fue 0
```
**Sin `DELETE`** (rol scoped): la red de seguridad es el **guard intra-txn** (aborta antes del COMMIT). Una vez commiteado, revierte **Eduardo** con los DELETE de arriba.

---

## 5. Post-carga
- Revisar en la app/UI (listados de proveedores y artículos, búsqueda por clave, render de enums/moneda/categoría).
- Procesar la **lista de revisión** (filas incompletas de §2b): corregir en SAP y re-cargar en una pasada posterior (mismo procedimiento, filtrando solo esas claves).
- Limpiar el staging si se desea: `DROP TABLE staging_carga.proveedores_sap, staging_carga.articulos_sap;` (las vistas dependen de ellas).

---

## 6. Notas de seguridad
- ⚠️ **Rotar el login read-only de SAP** (`user`): se expuso en texto durante el diseño. Es read-only en LAN, riesgo acotado, pero rotarlo es lo limpio.
- Passwords nunca en git: SAP por `SQLCMDPASSWORD` inline (muere con el shell); Postgres por `pgpass.conf` fuera del repo.
- CSVs con PII (nombres/RFC/emails) **fuera del repo**; borrarlos al terminar.

---

## 7. Estado de validación (al escribir este runbook)
| Entorno | Proveedores | Artículos | Esquema clave proveedor |
|---|---|---|---|
| local `millet_dev` | 2925 (total 3033) | 1452 Fase 1 (total 1529) | `P######` (prueba) |
| Azure dev | 2925 (total 2930) | 1452 Fase 1 (total 1462) | `P######` (prueba) |
| **Prod** | _pendiente_ | _pendiente_ | 🅿️ `{prefijo}-{NNNNNN}` |

> ⚠️ Lo único **no ejercitado** todavía: el esquema `{prefijo}-{NNNNNN}` y el universo completo de artículos (~12,984). Recomendado: un **ensayo limpio en Azure dev con la variante de prod** (Eduardo resetea las tablas dev primero, ya que el rol no tiene DELETE) antes de ejecutar en prod.
