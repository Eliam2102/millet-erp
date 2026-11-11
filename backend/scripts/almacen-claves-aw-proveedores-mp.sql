-- =============================================================================
-- Claves A+W de los proveedores de materia prima (canal Almacenes <-> ERP, F2)
-- =============================================================================
-- La app de Almacenes manda en REQUISICION_ENVIADA la clave A+W del proveedor
-- (datos.proveedor.claveAw) y el ERP lo busca por compartido.proveedores.clave_aw.
--
-- Fuentes:
--   * Jorge Toache (TI), 10-nov-2026: Proveedores_AW_compras.xlsx (proveedores de
--     vidrio y herrajes en A+W, columna «Número» = clave A+W).
--   * Fernando Alejos (Costos), 11-nov-2026 12:51: Relacion_Proveed_MP_RFC.xlsx
--     (19 proveedores de MP que se compran en SAP, código SAP = clave_legacy).
--
-- Solo se ligan los que coinciden sin duda (RFC o nombre exacto contra el
-- proveedor cargado de SAP). Idempotente: no pisa una clave_aw ya puesta ni una
-- clave usada por otro proveedor (índice único ux_proveedores_clave_aw).
--
-- Sin ligar (quedan para revisar con Compras/Fernando):
--   * A+W 111 y 235 «SAINT-GOBAIN-GLASS MEXICO» (RFC SGG950713A6A / XEXX): en el
--     ERP no hay un proveedor con ese RFC; Fernando lista P0552/P1680 «SAINT GOBAIN
--     MEXICO» con RFC SME9502015F6, que tampoco están cargados en el ERP.
--   * A+W 116 CASA GALVAN DE MEXICO (CGM000817PZ2) y 127 ALMA ROSA SANCHEZ LOPEZ:
--     sin proveedor con ese RFC en el ERP.
--   * P0992 TECGLASS, S.L. (Fernando): no está cargado en el ERP.
--
-- Uso: psql contra la BD del ambiente. Verificar con la consulta del final.

WITH mapa(clave_legacy, clave_aw, prueba) AS (VALUES
    ('P0320', 110,   'RFC GIV021204B1A'),
    ('P0988', 177,   'RFC KME140224BV8'),
    ('P1773', 237,   'RFC VPM5507058M9'),
    ('P0687', 217,   'nombre VITRO FLAT GLASS, LLC'),
    ('P1436', 124,   'nombre STANDARD GLAS (GIBRALTAR) LTD'),
    ('P1777', 238,   'nombre ASSA ABLOY OLIMPIA HARDWARE'),
    ('P1652', 234,   'nombre KOMMERLING CHEMISCHE FABRIK GMBH'),
    ('P2660', 29513, 'nombre GUARDIAN INDUSTRIES, LLC'),
    ('P0487', 215,   'nombre PILKINGTON NORTH AMERICA'),
    ('P0326', 135,   'RFC HER000222944'),
    ('P0141', 172,   'RFC CBI0004173V0'),
    ('P1631', 233,   'RFC TOR1909129L4'),
    ('P0611', 221,   'RFC DVL910131HF3')
)
UPDATE compartido.proveedores p
   SET clave_aw = m.clave_aw, updated_at = now(), updated_by = 'script-claves-aw-mp', version = p.version + 1
  FROM mapa m
 WHERE p.clave_legacy = m.clave_legacy
   AND p.deleted_at IS NULL
   AND p.clave_aw IS NULL
   AND NOT EXISTS (SELECT 1 FROM compartido.proveedores o WHERE o.clave_aw = m.clave_aw AND o.deleted_at IS NULL);

-- Verificación: los 19 de Fernando y los de A+W ya ligados.
SELECT clave_legacy, clave, rfc, razon_social, clave_aw
  FROM compartido.proveedores
 WHERE deleted_at IS NULL
   AND (clave_aw IS NOT NULL OR clave_legacy IN ('P0552','P0687','P1680','P1773','P0320','P1436','P1777','P0988','P0985',
        'P0848','P1652','P0138','P0992','P2156','P1228','P2902','P0671','P2382','P0039'))
 ORDER BY clave_aw NULLS LAST, clave_legacy;
