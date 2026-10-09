-- ============================================================
-- 02_load_articulos_local.sql  (PostgreSQL LOCAL, millet_dev)
-- Carga de artículos: staging -> vista de transformación -> dry-run -> INSERT.
-- G1.12: Alcance completo (inventariables y servicios). clave = ItemCode.
-- Idempotente (ON CONFLICT DO NOTHING).
-- Ejecutar por bloques. El INSERT (D) solo DESPUÉS de revisar el dry-run (C).
-- ============================================================

-- (A) Staging -------------------------------------------------
CREATE SCHEMA IF NOT EXISTS staging_carga;
DROP TABLE IF EXISTS staging_carga.articulos_sap CASCADE;
CREATE TABLE staging_carga.articulos_sap (
    item_code      text,
    nombre         text,
    unidad_medida  text,
    invnt_item     text,   -- 'Y' / 'N'
    grupo_cod      int,
    categoria      text,
    ultima_compra  date
);

-- Cargar el CSV exportado por 01_extract (ajusta la ruta):
-- \copy staging_carga.articulos_sap FROM 'articulos_sap.csv' WITH (FORMAT csv, HEADER true, FORCE_NULL (grupo_cod, categoria, ultima_compra));

-- (B) Vista de transformación (misma lógica para dry-run e INSERT) -----------
CREATE OR REPLACE VIEW staging_carga.v_articulos_carga AS
WITH con_dup AS (
    SELECT
        s.*,
        ROW_NUMBER() OVER (PARTITION BY TRIM(s.item_code) ORDER BY s.item_code) AS rn
    FROM staging_carga.articulos_sap s
)
SELECT
    TRIM(s.item_code)                              AS clave,
    TRIM(s.nombre)                                 AS nombre,
    TRIM(s.unidad_medida)                          AS unidad_medida_default,
    CASE WHEN s.invnt_item = 'N' THEN 1            -- no-inventario -> Servicio (GAP-9 / CA2.10)
         ELSE 0 END                                AS naturaleza,             -- inventariable -> Estándar
    0                                              AS estatus,                -- Activo
    s.categoria,
    s.grupo_cod,
    s.ultima_compra,
    CASE
        WHEN length(TRIM(s.item_code)) > 20 THEN 'clave>20'
        WHEN length(TRIM(s.nombre)) > 254 THEN 'nombre>254'
        WHEN length(TRIM(s.unidad_medida)) > 20 THEN 'uom>20'
        WHEN length(s.categoria) > 100 THEN 'categoria>100'
        WHEN s.rn > 1 THEN 'dup en lote'
        WHEN EXISTS (
            SELECT 1 FROM compartido.producto_aw paw
            WHERE paw.referencia_externa = TRIM(s.item_code)
        ) OR EXISTS (
            SELECT 1 FROM compartido.producto_aw_componente pac
            WHERE pac.componente_ref = TRIM(s.item_code)
        ) OR s.grupo_cod BETWEEN 142 AND 147 THEN 'posible A+W'  -- grupos SAP «VIDRIO *»: marcadores del vidrio que vive en A+W
        ELSE NULL
    END                                            AS motivo_exclusion
FROM con_dup s;

-- (C) DRY-RUN: revisa antes de insertar (NO inserta) -------------------------

-- Bloque 1: Resumen general y exclusiones
SELECT
    (SELECT count(*) FROM staging_carga.articulos_sap) AS total_staging,
    (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE motivo_exclusion IS NULL) AS aptos_insertar,
    (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE motivo_exclusion IS NOT NULL) AS total_excluidos,
    (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE motivo_exclusion = 'dup en lote') AS dup_en_lote,
    (SELECT count(*) FROM staging_carga.v_articulos_carga v
       JOIN compartido.articulos a ON a.clave = v.clave
      WHERE v.motivo_exclusion IS NULL) AS ya_existentes_se_omitiran,
    (SELECT count(*) FROM staging_carga.v_articulos_carga v
       JOIN compartido.articulos a ON a.clave = v.clave
      WHERE v.motivo_exclusion IS NULL AND (a.naturaleza != v.naturaleza OR a.nombre != v.nombre)) AS existentes_con_diferencias;

-- Bloque 2: Totales por grupo y naturaleza (Estándar vs Servicio)
SELECT
    COALESCE(grupo_cod::text, 'SIN GRUPO') AS grupo_cod,
    COALESCE(categoria, 'SIN CATEGORIA') AS categoria,
    count(*) FILTER (WHERE naturaleza = 0) AS estandar_invnt,
    count(*) FILTER (WHERE naturaleza = 1) AS servicio,
    count(*) AS total_grupo
FROM staging_carga.v_articulos_carga
WHERE motivo_exclusion IS NULL
GROUP BY grupo_cod, categoria
ORDER BY grupo_cod, categoria;

-- Bloque 3: Unidades fuera del mapa limpio (CTE mapa)
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
SELECT
    v.unidad_medida_default AS uom_cruda,
    count(*) AS articulos_con_esta_uom
FROM staging_carga.v_articulos_carga v
LEFT JOIN mapa m
  ON upper(btrim(regexp_replace(v.unidad_medida_default, '\.$', ''))) = m.legacy_norm
WHERE v.motivo_exclusion IS NULL
  AND m.codigo IS NULL
GROUP BY v.unidad_medida_default
ORDER BY articulos_con_esta_uom DESC;

-- Bloque 4: Detalle de motivos de exclusión (longitud, A+W, duplicados)
SELECT
    motivo_exclusion,
    count(*) AS total
FROM staging_carga.v_articulos_carga
WHERE motivo_exclusion IS NOT NULL
GROUP BY motivo_exclusion
ORDER BY total DESC;

-- Bloque 5: Conteo de actividad (sin compra en 24 meses)
SELECT
    count(*) FILTER (WHERE ultima_compra IS NULL) AS sin_fecha_compra,
    count(*) FILTER (WHERE ultima_compra < CURRENT_DATE - INTERVAL '24 months') AS sin_compra_24m,
    count(*) FILTER (WHERE ultima_compra >= CURRENT_DATE - INTERVAL '24 months') AS compra_reciente_24m
FROM staging_carga.v_articulos_carga
WHERE motivo_exclusion IS NULL;

-- (D) INSERT REAL (IDEMPOTENTE) ------------------------------
-- Tabla de auditoría para rollback seguro de lo insertado en este lote:
CREATE TABLE IF NOT EXISTS staging_carga.articulos_insertados (
    clave varchar(20) PRIMARY KEY,
    inserted_at timestamptz NOT NULL DEFAULT now()
);

WITH insertados AS (
    INSERT INTO compartido.articulos
        (id, clave, nombre, unidad_medida_default, naturaleza, estatus, categoria,
         version, created_at, updated_at)
    SELECT
        gen_random_uuid(),
        v.clave,
        v.nombre,
        v.unidad_medida_default,
        v.naturaleza,
        v.estatus,
        v.categoria,
        1,
        now(),
        now()
    FROM staging_carga.v_articulos_carga v
    WHERE v.motivo_exclusion IS NULL
    ON CONFLICT (clave) DO NOTHING
    RETURNING clave
)
INSERT INTO staging_carga.articulos_insertados (clave)
SELECT clave FROM insertados;

-- (E) RECONCILIACIÓN POST-CARGA -------------------------------
-- 1. Ejecutar Script 1 de docs/operacion/reconciliacion-unidades.md
-- 2. Ejecutar Sección 2 (APLICAR) de docs/operacion/reconciliacion-categorias.md

-- (F) VERIFICACIÓN POST-CARGA --------------------------------
SELECT
    count(*) AS total_en_catalogo,
    count(*) FILTER (WHERE naturaleza = 0) AS estandar_inventariables,
    count(*) FILTER (WHERE naturaleza = 1) AS servicios,
    count(*) FILTER (WHERE unidad_medida_id IS NOT NULL) AS con_unidad_reconciliada,
    count(*) FILTER (WHERE categoria_id IS NOT NULL) AS con_categoria_reconciliada
FROM compartido.articulos;

-- ------------------------------------------------------------
-- ROLLBACK SEGURO:
--   DELETE FROM compartido.articulos a
--   USING staging_carga.articulos_insertados ins
--   WHERE a.clave = ins.clave;
-- ------------------------------------------------------------
