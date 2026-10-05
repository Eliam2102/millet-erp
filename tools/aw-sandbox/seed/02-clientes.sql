SET NOCOUNT ON;
-- Registro nulo de A+W (ID 0): el lector pagina con ID > 0, no debe salir.
INSERT SYSADM.KU_KUNDEN (ID, MANDANT, NAME1, NAME2, NAME3, STRASSE, ORT, PLZ, LAND, TLF1, TLF2, MAIL, WAEHRUNG, ZAHLBED, UST_ID, KZ_STATUS, KZ_GESPERRT, DATUM, PROVINZ, STEUERNUMMER)
VALUES (0, 1, N'<indf>', N'', N'', N'', N'', N'', N'MEX', N'', N'', N'', N'<indf>', N'<indf>', N'', 0, 0, '2000-01-01', N'', N'');

;WITH n AS (SELECT TOP (150) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT SYSADM.KU_KUNDEN (ID, MANDANT, NAME1, NAME2, NAME3, STRASSE, ORT, PLZ, LAND, TLF1, TLF2, MAIL, WAEHRUNG, ZAHLBED, UST_ID,
                         KZ_STATUS, KZ_GESPERRT, DATUM, PROVINZ, STEUERNUMMER, KREDIT_LIMIT, KREDIT_LIMIT1, TRANSACTION_TIME, KREDIT_LIMIT_NET)
SELECT i, 1,
  CASE WHEN i = 140 THEN N'' ELSE N'CLIENTE DEMO ' + RIGHT(N'000' + CAST(i AS nvarchar(10)), 3) END,   -- NAME1 vacío (real: 10)
  CASE WHEN i % 40 = 0 THEN N'SA DE CV DEMO' ELSE N'' END,
  CASE WHEN i % 15 = 0 THEN N'CONTACTO DEMO' ELSE N'' END,
  CASE WHEN i % 4 = 0 THEN N'CALLE DEMO ' + CAST(i AS nvarchar(10)) ELSE N'' END,
  CASE WHEN i % 30 = 0 THEN N'' ELSE N'CIUDAD DEMO' END,
  CASE WHEN i % 5 = 0 THEN N'' ELSE N'0' + RIGHT(N'0000' + CAST(i AS nvarchar(10)), 4) END,
  N'MEX',
  CASE WHEN i % 3 = 0 THEN N'' ELSE N'55' + RIGHT(N'00000000' + CAST(i AS nvarchar(10)), 8) END,
  N'',
  CASE WHEN i % 3 = 0 THEN N'' ELSE N'cliente' + RIGHT(N'000' + CAST(i AS nvarchar(10)), 3) + N'@demo.invalid' END,
  CASE WHEN i = 77 THEN N'Euro' WHEN i % 50 = 7 THEN N'USD' WHEN i % 3 = 0 THEN N'<indf>' ELSE N'PESOSMX' END,
  CASE WHEN i % 53 = 0 THEN N'contado'            -- minúscula: no coincide (CS_AS)
       WHEN i % 47 = 0 THEN N'CREDITO DEMO'       -- ZAHLBED sin coincidencia en KA_ZAHLBED
       WHEN i % 13 = 0 THEN N'REPARTO' WHEN i % 10 = 1 THEN N'<indf>' WHEN i % 17 = 0 THEN N'45 DIAS'
       WHEN i % 19 = 0 THEN N'30 DIAS' WHEN i % 23 = 0 THEN N'60 DIAS' WHEN i = 29 THEN N'90 DIAS' ELSE N'CONTADO' END,
  CASE WHEN i % 12 = 0 THEN N''                                    -- sin RFC
       WHEN i % 3 = 0 THEN N'XAXX010101000'                        -- RFC genérico repetido
       WHEN i % 11 = 0 THEN N'XEXX010101000'
       ELSE N'DEMO' + RIGHT(N'000' + CAST(i AS nvarchar(10)), 3) END,
  CASE WHEN i % 8 < 3 THEN 1 ELSE 2 END,
  CASE WHEN i % 5 = 0 THEN 0 ELSE 1 END,
  DATEADD(DAY, i, CAST('2020-01-01' AS date)),
  CASE WHEN i % 20 = 0 THEN N'' ELSE N'CDMX' END,
  CASE WHEN i % 4 = 0 THEN N'' ELSE N'DEMO' + RIGHT(N'000' + CAST(i AS nvarchar(10)), 3) END,
  CASE WHEN i % 25 = 0 THEN 0 WHEN i % 6 = 0 THEN 50000 ELSE NULL END,
  CASE WHEN i % 25 = 0 THEN 0 WHEN i % 6 = 0 THEN 25000 ELSE NULL END,
  CASE WHEN i % 40 = 0 THEN CAST('2026-01-01T10:00:00' AS datetime) ELSE NULL END,
  CASE WHEN i % 6 = 0 THEN 50000.0 ELSE NULL END
FROM n;
GO
