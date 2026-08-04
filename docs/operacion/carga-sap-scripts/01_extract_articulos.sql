-- ============================================================
-- 01_extract_articulos.sql  (SQL Server / SAP, READ-ONLY)
-- Extractor de ARTÍCULOS activos desde OITM, FASE 1 de deploy.
-- Alcance de esta fase: solo grupos papelería + limpieza + insumos.
--   ItmsGrpCod IN (101,102,103,104,115,116,120,130)
--   (101/102/103 papelería-oficina · 104 limpieza · 115/116/120/130 insumos)
-- Filtro base: materiales activos, con nombre y uom (excluye activos fijos 'F',
--   sin-nombre y sin-uom, según las decisiones tomadas).
-- Universo esperado: 1,452 (todos InvntItem='Y' -> naturaleza=0 Estándar).
-- Salida: una fila por artículo; se exporta a CSV para el staging en Postgres.
-- (En PROD se quita el filtro de grupo y van todos los no-cancelados.)
-- ============================================================

SELECT
    T0.ItemCode                              AS item_code,      -- -> clave (tal cual; dedup por ix_articulos_clave)
    T0.ItemName                              AS nombre,         -- NOT NULL en destino
    T0.InvntryUom                            AS unidad_medida,  -- -> unidad_medida_default (NOT NULL)
    T0.InvntItem                             AS invnt_item,     -- 'Y'/'N' -> naturaleza (se deriva en el load)
    T0.ItmsGrpCod                            AS grupo_cod,      -- traza del grupo
    T1.ItmsGrpNam                            AS categoria       -- -> categoria (texto; cabe en varchar(100))
FROM OITM T0
JOIN OITB T1 ON T0.ItmsGrpCod = T1.ItmsGrpCod
WHERE T0.validFor  = 'Y'
  AND T0.frozenFor = 'N'
  AND T0.ItemType  = 'I'                                        -- excluye activos fijos ('F')
  AND NULLIF(LTRIM(RTRIM(T0.ItemName)),'')   IS NOT NULL        -- excluye sin nombre
  AND NULLIF(LTRIM(RTRIM(T0.InvntryUom)),'') IS NOT NULL        -- excluye sin uom
  AND T0.ItmsGrpCod IN (101,102,103,104,115,116,120,130)        -- <-- FASE 1: solo estos grupos
ORDER BY T0.ItmsGrpCod, T0.ItemCode;

-- ------------------------------------------------------------
-- Export a CSV (articulos_sap.csv), mismo mecanismo que proveedores (CC):
--   sqlcmd con CSV construido en SQL (comillas dobles + escape ""), -i con ruta
--   Windows (cygpath -w + MSYS2_ARG_CONV_EXCL='*'), salida -u (UTF-16) -> iconv UTF-8.
--   ItemName/categoria pueden traer comas/acentos -> CSV bien citado y UTF-8.
--   Working dir fuera del repo: C:\Users\Victor\carga_articulos_dev\ (NO versionar el CSV).
--   Verifica: 1,452 filas + header, 6 campos, UTF-8.
-- ------------------------------------------------------------
