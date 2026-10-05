SET NOCOUNT ON;
-- Registro nulo (BA_PRODUKT = 0): excluido por el lector (> 0).
INSERT SYSADM.BA_PRODUKTE (BA_PRODUKT, BA_MCODE, BA_PRODUKTART, BA_STD_HOEHE, BA_STD_BREITE, KZ_GESPERRT, BA_MASS_DICKE)
VALUES (0, N'', N'<indf>', 0, 0, 0, 0);
INSERT SYSADM.BA_PRODUKTE_BEZ (SPRACH_ID, BA_PRODUKT, BA_BEZ1, BA_BEZ2, BA_BEZ3, BA_MENGENEINH) VALUES (0, 0, N'<indf>', N'', N'', N'<indf>');

-- 1..6 = componentes de la composición: vidrio 6, PVB 0.89, vidrio 3, cámara 12, proceso (sin espesor), kit (sin espesor).
;WITH n AS (SELECT TOP (160) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b),
p AS (
  SELECT i,
    CASE i WHEN 1 THEN N'Vidrio plano' WHEN 2 THEN N'Relleno' WHEN 3 THEN N'Vidrio plano' WHEN 4 THEN N'Perfil intercalario'
           WHEN 5 THEN N'Proceso' WHEN 6 THEN N'Producto'
           WHEN 151 THEN N'Barrotillos' WHEN 152 THEN N'Caja' WHEN 153 THEN N'Construcc./muros de vidrio' WHEN 154 THEN N'Vidrio curvo'
           ELSE CHOOSE(i % 20 + 1, N'Producto', N'Producto', N'Producto', N'Producto', N'Producto', N'Producto',
                       N'VTE', N'VTE', N'VTE', N'VLA', N'VC', N'Vidrio plano', N'Vidrio plano', N'Vidrio plano',
                       N'Proceso', N'Relleno', N'Formas', N'Perfil intercalario', N'Servicios/recargos', N'VTE') END AS art,
    CASE i WHEN 1 THEN 6 WHEN 2 THEN 0.89 WHEN 3 THEN 3 WHEN 4 THEN 12 WHEN 5 THEN 0 WHEN 6 THEN 0
           ELSE CASE WHEN i % 9 = 0 THEN 12.89 WHEN i % 9 = 3 THEN 18 WHEN i % 2 = 0 THEN 0 ELSE 6 END END AS dicke
  FROM n)
INSERT SYSADM.BA_PRODUKTE (BA_PRODUKT, BA_MCODE, BA_PRODUKTART, BA_STD_HOEHE, BA_STD_BREITE, KZ_GESPERRT, BA_MASS_DICKE, TRANSACTION_TIME)
SELECT i,
  N'PRODUCTO DEMO ' + RIGHT(N'000' + CAST(CASE WHEN i > 6 AND i % 10 = 0 THEN i - 1 ELSE i END AS nvarchar(10)), 3),  -- BA_MCODE repetido
  art,
  CASE WHEN i % 25 = 0 THEN 2400 ELSE 0 END, CASE WHEN i % 25 = 0 THEN 1200 ELSE 0 END,   -- medidas 0 = sin dato
  CASE WHEN i = 77 THEN 2 WHEN i % 5 = 4 THEN 1 ELSE 0 END,
  dicke,
  CASE WHEN i % 20 = 0 THEN CAST('2026-01-01T10:00:00' AS datetime) ELSE NULL END
FROM p;

-- Descripción SPRACH_ID 0 (excepto 141..144: producto sin BEZ de idioma 0, como el real). 50/100/120: descripción vacía => respaldo BA_MCODE.
;WITH n AS (SELECT TOP (160) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT SYSADM.BA_PRODUKTE_BEZ (SPRACH_ID, BA_PRODUKT, BA_BEZ1, BA_BEZ2, BA_BEZ3, BA_MENGENEINH)
SELECT 0, i,
  CASE WHEN i IN (50, 100, 120) THEN N'' ELSE N'PRODUCTO DEMO ' + RIGHT(N'000' + CAST(i AS nvarchar(10)), 3) END,
  CASE WHEN i IN (50, 100, 120) THEN N'' WHEN i % 7 = 0 THEN N'COLOR DEMO' ELSE N'' END,
  CASE WHEN i IN (50, 100, 120) THEN N'' WHEN i % 4 = 0 THEN N'DETALLE DEMO' ELSE N'' END,
  CASE WHEN i IN (1, 2, 3) THEN N'm' + NCHAR(178) WHEN i = 4 THEN N'm lin.' WHEN i IN (5, 6) THEN N'Pza'
       WHEN i = 155 THEN N'm' WHEN i = 156 THEN N'm' + NCHAR(179)
       ELSE CHOOSE(i % 35 + 1, N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178),
            N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178),
            N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178),
            N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178), N'm' + NCHAR(178),
            N'Pza', N'Pza', N'Pza', N'Pza', N'Pza', N'Pza', N'Pza', N'Pza', N'Pza', N'Pza',
            N'm lin.', N'm lin.', N'Kg', N'ltr', N'<indf>') END
FROM n WHERE i NOT IN (141, 142, 143, 144);
-- Segundo idioma (SPRACH_ID 1) para ~1/3.
INSERT SYSADM.BA_PRODUKTE_BEZ (SPRACH_ID, BA_PRODUKT, BA_BEZ1, BA_BEZ2, BA_BEZ3, BA_MENGENEINH)
SELECT 1, BA_PRODUKT, N'DEMO PRODUCT ' + RIGHT(N'000' + CAST(BA_PRODUKT AS nvarchar(10)), 3), N'', N'', BA_MENGENEINH
FROM SYSADM.BA_PRODUKTE_BEZ WHERE SPRACH_ID = 0 AND BA_PRODUKT > 6 AND BA_PRODUKT % 3 = 0;

-- Composición BA_STUKL (sin PK, como el real). Nivel 1 = lo que lee el sincronizador; nivel 2 = ruido que debe ignorarse.
;WITH n AS (SELECT TOP (160) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT SYSADM.BA_STUKL (PRODUKT, BOM_POS, BOM_PRODUKT, BOM_LEVEL)
SELECT n.i, c.pos, c.comp, c.lvl
FROM n
JOIN (VALUES (0,1,1,1),(0,2,2,1),(0,3,1,1),            -- laminado 6+0.89+6
            (3,1,3,1),(3,2,4,1),(3,3,3,1),            -- aislante 3+12+3
            (6,1,1,1),(6,2,5,1),(6,3,6,1),            -- capa de proceso y kit sin espesor => "6"
            (0,4,4,2),(3,4,1,2)) AS c(m, pos, comp, lvl) ON n.i % 9 = c.m
WHERE n.i > 6;
GO
