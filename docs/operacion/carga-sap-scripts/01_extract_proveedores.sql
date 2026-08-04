-- ============================================================
-- 01_extract_proveedores.sql  (SQL Server / SAP, READ-ONLY)
-- Extractor de proveedores ACTIVOS desde OCRD.
-- Filtro: proveedores reales (CardType='S' + grupo PROV.* + activos).
-- Universo esperado: ~2,925 (NAC ~2723 + EXT ~202).
-- Salida: una fila por proveedor; se exporta a CSV para el staging en Postgres.
-- ============================================================

SELECT
    T0.CardCode                                AS card_code,        -- -> clave_legacy (traza)
    T1.GroupName                               AS group_name,       -- 'PROV. NACIONALES' / 'PROV. EXTRANJEROS'
    CASE WHEN T1.GroupName LIKE 'PROV. NACIONALES%'  THEN 'NAC'
         WHEN T1.GroupName LIKE 'PROV. EXTRANJEROS%' THEN 'EXT'
         ELSE LEFT(LTRIM(REPLACE(T1.GroupName,'PROV.','')),3) END AS prefijo,  -- prod lo usa; la prueba lo ignora
    T0.CardName                                AS razon_social,
    NULLIF(LTRIM(RTRIM(T0.CardFName)),'')      AS nombre_comercial, -- si NULL -> CardName en el load
    NULLIF(LTRIM(RTRIM(T0.LicTradNum)),'')     AS rfc,              -- real, o XEXX010101000
    NULLIF(LTRIM(RTRIM(T0.E_Mail)),'')         AS email,
    NULLIF(LTRIM(RTRIM(T0.Phone1)),'')         AS telefono,
    T0.Currency                                AS currency,         -- 'MXP','##','USD','EUR'
    ISNULL(T2.ExtraDays,0) + ISNULL(T2.ExtraMonth,0)*30 AS condiciones_pago_dias
FROM OCRD T0
JOIN OCRG T1 ON T0.GroupCode = T1.GroupCode AND T1.GroupType = 'S'   -- grupos de proveedor
LEFT JOIN OCTG T2 ON T0.GroupNum = T2.GroupNum                       -- condiciones de pago
WHERE T0.CardType = 'S'
  AND T1.GroupName LIKE 'PROV.%'   -- incluye NACIONALES/EXTRANJEROS, excluye ACREEDORES
  AND T0.validFor  = 'Y'
  AND T0.frozenFor = 'N'
ORDER BY T1.GroupName, T0.CardCode;

-- ------------------------------------------------------------
-- Export a CSV (mecanismo usado por CC, sin PowerShell):
--   PowerShell está bloqueado por deny-rule del entorno y no hay Python/pyodbc.
--   Se usa sqlcmd con el CSV CONSTRUIDO en SQL (comillas dobles + escape "" ),
--   garantizando CSV bien citado aunque razon_social traiga comas/comillas.
--   Ver `_export_query.sql` (misma lógica de filtro/derivación, una columna csv_line):
--     sqlcmd -S <SAP_HOST> -d <SAP_DB> -U <SAP_USER> -C -h -1 -W -k 1 -y 0 -w 65535 -f 65001 \
--            -i _export_query.sql -o proveedores_sap.csv
--   El CSV incluye header (\copy ... HEADER true lo ignora). UTF-8.
-- ------------------------------------------------------------
