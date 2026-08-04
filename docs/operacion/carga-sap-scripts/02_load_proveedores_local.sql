-- ============================================================
-- 02_load_proveedores_local.sql  (PostgreSQL LOCAL)
-- Carga de proveedores: staging -> vista de transformación -> dry-run -> INSERT.
-- PRUEBA: clave = P + consecutivo. One-shot (no idempotente; ver nota al final).
-- Ejecutar por bloques. El INSERT (D) solo después de revisar el dry-run (C).
--
-- CC: ANTES del INSERT, verifica con \d compartido.proveedores que existan los
-- nombres de columna usados abajo (en duda: nombre_comercial, clave_legacy,
-- condiciones_pago_dias, moneda_preferida_id). Ajusta/quita cualquier mismatch.
-- ============================================================

-- (A) Staging -------------------------------------------------
CREATE SCHEMA IF NOT EXISTS staging_carga;
DROP TABLE IF EXISTS staging_carga.proveedores_sap CASCADE;
CREATE TABLE staging_carga.proveedores_sap (
    card_code              text,
    group_name             text,
    prefijo                text,
    razon_social           text,
    nombre_comercial       text,
    rfc                    text,
    email                  text,
    telefono               text,
    currency               text,
    condiciones_pago_dias  int
);

-- Cargar el CSV exportado por 01_extract (ajusta la ruta):
-- \copy staging_carga.proveedores_sap FROM 'proveedores_sap.csv' WITH (FORMAT csv, HEADER true);

-- (B) Vista de transformación (misma lógica para dry-run e INSERT) -----------
CREATE OR REPLACE VIEW staging_carga.v_proveedores_carga AS
SELECT
    'P' || lpad((row_number() OVER (ORDER BY s.group_name, s.card_code))::text, 6, '0') AS clave,
    s.card_code                                   AS clave_legacy,
    s.razon_social,
    COALESCE(s.nombre_comercial, s.razon_social)  AS nombre_comercial,
    s.rfc,
    CASE WHEN s.rfc IS NULL OR s.rfc = 'XEXX010101000' THEN 0   -- moral (extranjeros/genérico)
         WHEN length(s.rfc) = 13 THEN 1                         -- física
         ELSE 0 END                               AS tipo_persona,   -- 12 -> moral
    0                                             AS estatus,        -- Activo
    s.email,
    s.telefono,
    s.condiciones_pago_dias,
    m.id                                          AS moneda_preferida_id
FROM staging_carga.proveedores_sap s
LEFT JOIN compartido.monedas m
    ON m.codigo = CASE WHEN s.currency = 'MXP' THEN 'MXN'   -- normaliza
                       WHEN s.currency = '##'  THEN NULL    -- multimoneda -> sin moneda
                       ELSE s.currency END;                 -- USD/EUR

-- (C) DRY-RUN: revisa antes de insertar (NO inserta) -------------------------
SELECT
    (SELECT count(*) FROM staging_carga.proveedores_sap)                       AS filas_staging,
    (SELECT count(*) FROM staging_carga.v_proveedores_carga)                   AS a_insertar,
    (SELECT count(*) FROM staging_carga.v_proveedores_carga WHERE tipo_persona = 1) AS fisicas,
    (SELECT count(*) FROM staging_carga.v_proveedores_carga WHERE moneda_preferida_id IS NULL) AS sin_moneda,
    -- moneda real (no ##) que no resolvió en el catálogo -> debe ser 0:
    (SELECT count(*) FROM staging_carga.proveedores_sap s
       WHERE s.currency NOT IN ('##')
         AND NOT EXISTS (SELECT 1 FROM compartido.monedas m
                          WHERE m.codigo = CASE WHEN s.currency='MXP' THEN 'MXN' ELSE s.currency END)) AS moneda_no_catalogo,
    -- colisión de clave con lo ya existente -> debe ser 0:
    (SELECT count(*) FROM staging_carga.v_proveedores_carga v
       JOIN compartido.proveedores p ON p.clave = v.clave)                     AS colision_clave,
    -- info: cuántos RFC reales ya existen en destino (no bloquea la prueba):
    (SELECT count(*) FROM staging_carga.v_proveedores_carga v
       JOIN compartido.proveedores p ON p.rfc = v.rfc
      WHERE v.rfc <> 'XEXX010101000')                                          AS rfc_ya_existe_info;

-- Revisa: colision_clave = 0 y moneda_no_catalogo = 0 antes de continuar.

-- (D) INSERT real (solo si el dry-run se ve bien) ----------------------------
INSERT INTO compartido.proveedores
    (id, clave, clave_legacy, razon_social, nombre_comercial, rfc, tipo_persona,
     estatus, email, telefono, condiciones_pago_dias, moneda_preferida_id,
     version, created_at, updated_at)
SELECT
    gen_random_uuid(), clave, clave_legacy, razon_social, nombre_comercial, rfc, tipo_persona,
    estatus, email, telefono, condiciones_pago_dias, moneda_preferida_id,
    1, now(), now()
FROM staging_carga.v_proveedores_carga;

-- (E) Verificación post-carga
-- SELECT count(*) FROM compartido.proveedores;                       -- = 108 (previo) + insertados
-- SELECT clave, clave_legacy, rfc, tipo_persona, moneda_preferida_id
--   FROM compartido.proveedores WHERE clave LIKE 'P%' ORDER BY clave LIMIT 10;

-- ------------------------------------------------------------
-- NOTAS
-- * One-shot: el row_number arranca en P000001. Re-correr el INSERT colisiona
--   (clave única). Para repetir, primero limpia las filas de prueba o continúa
--   el consecutivo desde el max existente. (En prod el runbook usa el esquema
--   de grupo {prefijo}-{NNNNNN} y maneja la continuación.)
-- * Sin dedup en la primera carga (insertan todas; rfc no es único). El dedup
--   por RFC para re-imports queda como tema del runbook de prod (los datos
--   mostraron que RFC es mala llave: XEXX compartido + 39 RFC reales repetidos).
-- * created_by/updated_by se omiten (nullable). Si tu \d los marca NOT NULL,
--   agrégalos con un valor de marca (p.ej. 'carga-sap-dev').
-- ------------------------------------------------------------
