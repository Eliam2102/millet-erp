-- ============================================================
-- 01_extract_articulos.sql  (SQL Server / SAP, READ-ONLY)
-- Extractor de ARTÍCULOS activos desde OITM (G1.12: alcance completo).
-- Incluye inventariables (InvntItem='Y') y servicios/no-inventariables
-- (InvntItem='N').
-- Filtro base: artículos activos, no congelados, tipo item 'I' (excluye
--   activos fijos 'F'), con nombre y unidad de medida no vacíos.
-- LEFT JOIN OITB: para no perder artículos cuyo grupo no exista o sea nulo.
-- Salida: 7 campos por artículo; se exporta a CSV para staging en Postgres.
-- ============================================================

SELECT
    T0.ItemCode                              AS item_code,      -- -> clave (tal cual; dedup por ix_articulos_clave)
    T0.ItemName                              AS nombre,         -- NOT NULL en destino (varchar(254))
    T0.InvntryUom                            AS unidad_medida,  -- -> unidad_medida_default (varchar(20))
    T0.InvntItem                             AS invnt_item,     -- 'Y'/'N' -> naturaleza (0=Estándar, 1=Servicio)
    T0.ItmsGrpCod                            AS grupo_cod,      -- traza del grupo (nullable si no tiene)
    T1.ItmsGrpNam                            AS categoria,      -- -> categoria (varchar(100), nullable)
    T0.LastPurDat                            AS ultima_compra   -- -> fecha de última compra para conteo 24 meses
FROM OITM T0
LEFT JOIN OITB T1 ON T0.ItmsGrpCod = T1.ItmsGrpCod
WHERE T0.validFor  = 'Y'
  AND T0.frozenFor = 'N'
  AND T0.ItemType  = 'I'                                        -- excluye activos fijos ('F')
  AND NULLIF(LTRIM(RTRIM(T0.ItemName)),'')   IS NOT NULL        -- excluye sin nombre
  AND NULLIF(LTRIM(RTRIM(T0.InvntryUom)),'') IS NOT NULL        -- excluye sin uom
ORDER BY T0.ItmsGrpCod, T0.ItemCode;

-- ------------------------------------------------------------
-- Export a CSV (articulos_sap.csv):
--   sqlcmd con CSV construido en SQL (comillas dobles + escape "") o bcp,
--   salida UTF-8. 7 campos con encabezado.
--   Working dir fuera del repo: ~/millet-carga/ (NO versionar el CSV).
-- ------------------------------------------------------------
