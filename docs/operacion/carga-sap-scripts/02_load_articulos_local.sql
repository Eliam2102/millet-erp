-- ============================================================
-- 02_load_articulos_local.sql  (PostgreSQL LOCAL, millet_dev)
-- Carga de artículos: staging -> vista de transformación -> dry-run -> INSERT.
-- FASE 1: 1,452 artículos (papelería + limpieza + insumos). clave = ItemCode.
-- Ejecutar por bloques. El INSERT (D) solo DESPUÉS de revisar el dry-run (C).
--
-- ⚠️ CRÍTICO: clave = ItemCode y ya hay 77 articulos en local. El dry-run
--   verifica colision_clave contra esos 77. Si colision_clave > 0 -> PÁRATE y
--   repórtalo (no insertes); hay que decidir qué hacer con los choques.
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
    categoria      text
);

-- Cargar el CSV exportado por 01_extract (ajusta la ruta):
-- \copy staging_carga.articulos_sap FROM 'articulos_sap.csv' WITH (FORMAT csv, HEADER true);

-- (B) Vista de transformación (misma lógica para dry-run e INSERT) -----------
CREATE OR REPLACE VIEW staging_carga.v_articulos_carga AS
SELECT
    TRIM(s.item_code)                              AS clave,                  -- ItemCode tal cual
    s.nombre,
    s.unidad_medida                                AS unidad_medida_default,
    CASE WHEN s.invnt_item = 'N' THEN 1            -- no-inventario -> Servicio
         ELSE 0 END                                AS naturaleza,             -- resto -> Estándar
    0                                              AS estatus,                -- Activo
    s.categoria
FROM staging_carga.articulos_sap s;

-- (C) DRY-RUN: revisa antes de insertar (NO inserta) -------------------------
SELECT
    (SELECT count(*) FROM staging_carga.articulos_sap)                            AS filas_staging,
    (SELECT count(*) FROM staging_carga.v_articulos_carga)                        AS a_insertar,     -- esperado 1452
    (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE naturaleza = 0)   AS estandar,       -- esperado 1452
    (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE naturaleza = 1)   AS servicios,      -- esperado 0
    -- CRÍTICO: choque de clave (ItemCode) contra los 77 ya existentes -> debe ser 0:
    (SELECT count(*) FROM staging_carga.v_articulos_carga v
       JOIN compartido.articulos a ON a.clave = v.clave)                          AS colision_clave,
    -- sanity: categoria que no cabe en varchar(100) -> debe ser 0:
    (SELECT count(*) FROM staging_carga.v_articulos_carga WHERE length(categoria) > 100) AS categoria_larga,
    -- sanity: clave duplicada dentro del propio lote -> debe ser 0:
    (SELECT count(*) FROM (
        SELECT clave FROM staging_carga.v_articulos_carga GROUP BY clave HAVING count(*) > 1
     ) d)                                                                          AS dup_en_lote;

-- Revisa: colision_clave = 0, dup_en_lote = 0 y categoria_larga = 0 antes de continuar.

-- (D) INSERT real (solo si el dry-run se ve bien) ----------------------------
INSERT INTO compartido.articulos
    (id, clave, nombre, unidad_medida_default, naturaleza, estatus, categoria,
     version, created_at, updated_at)
SELECT
    gen_random_uuid(), clave, nombre, unidad_medida_default, naturaleza, estatus, categoria,
    1, now(), now()
FROM staging_carga.v_articulos_carga;

-- (E) Verificación post-carga ------------------------------------------------
-- SELECT count(*)                                  AS importados,   -- esperado 1452
--        count(*) FILTER (WHERE naturaleza = 1)    AS servicios,    -- esperado 0
--        count(*) FILTER (WHERE naturaleza = 0)    AS estandar      -- esperado 1452
-- FROM compartido.articulos a
-- WHERE EXISTS (SELECT 1 FROM staging_carga.articulos_sap s WHERE TRIM(s.item_code) = a.clave);
-- SELECT count(*) AS total_articulos FROM compartido.articulos;     -- esperado 77 + 1452 = 1529

-- ------------------------------------------------------------
-- NOTAS
-- * ROLLBACK SEGURO (válido porque colision_clave fue 0):
--     DELETE FROM compartido.articulos a USING staging_carga.articulos_sap s
--      WHERE a.clave = TRIM(s.item_code);
-- * One-shot. Re-correr el INSERT chocaría con ix_articulos_clave (clave única).
-- * naturaleza por InvntItem ('N'->Servicio). En esta fase todos 'Y' -> servicios=0.
-- * created_by/updated_by se omiten (nullable).
-- * PROD: se quita el filtro ItmsGrpCod IN (...); van todos los no-cancelados.
-- ------------------------------------------------------------
