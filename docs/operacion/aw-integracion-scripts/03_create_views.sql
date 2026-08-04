/* =============================================================================
   03_create_views.sql — Vistas de lectura para la ingesta de pedidos (ADR-0048)
   Ejecutar en: MILLET_INTEGRACION y MILLET_INTEGRACION_DEV (ambas apuntan a
   MILMAIN real; la _DEV existe para aislar la tabla-puente, no los datos).

   Idempotente vía stub (CREATE mínimo si no existe) + ALTER con la definición
   real. NO usar CREATE OR ALTER ni STRING_AGG: la instancia SERDATA\AWBUSINESS
   es SQL Server 2016 RTM SIN SP1 — 13.0.1601.5 Standard (CREATE OR ALTER
   requiere 2016 SP1; STRING_AGG requiere 2017).
   El patrón stub+ALTER además preserva los GRANT del 04 al re-ejecutar.

   ⚠️ ESTADO: BORRADOR DERIVADO DEL DISEÑO JSON 2026-05 (doc 04, Anexo A).
   El CONTRATO DE COLUMNAS (nombres/tipos expuestos) está CONGELADO — los
   adapters del ERP seleccionan por estos nombres. El SQL INTERNO se valida
   y ajusta con datos reales en la fase guiada (marcas -- VALIDAR:).

   Fuentes (catálogo campo-por-campo del diseño JSON):
   - Cabecera:    MILMAIN.SYSADM.BW_AUFTR_KOPF (+ KU_KUNDEN, KA_FIRMA_MITARB,
                  KA_OBJEKT, KA_LIEFERBED, KA_ZAHLWEG, KA_WAEHRUNGEN, BW_AUFTR_KTXT)
   - Posiciones:  MILMAIN.SYSADM.BW_AUFTR_POS (+ KA_LAGER_DEF)
   - Componentes: MILMAIN.SYSADM.BW_AUFTR_STKL
   ========================================================================== */

USE MILLET_INTEGRACION;  -- repetir con MILLET_INTEGRACION_DEV
GO

/* ------------------------- stubs (solo primera vez) --------------------- */
IF OBJECT_ID('dbo.fn_limpia', 'FN') IS NULL
    EXEC (N'CREATE FUNCTION dbo.fn_limpia(@s nvarchar(max)) RETURNS nvarchar(max) AS BEGIN RETURN @s END');
IF OBJECT_ID('dbo.fn_solo_alfanumerico', 'FN') IS NULL
    EXEC (N'CREATE FUNCTION dbo.fn_solo_alfanumerico(@s nvarchar(200)) RETURNS nvarchar(200) AS BEGIN RETURN @s END');
IF OBJECT_ID('dbo.vw_erp_pedido_cabecera', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_pedido_cabecera AS SELECT 1 AS stub');
IF OBJECT_ID('dbo.vw_erp_pedido_linea', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_pedido_linea AS SELECT 1 AS stub');
IF OBJECT_ID('dbo.vw_erp_pedido_componente', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_pedido_componente AS SELECT 1 AS stub');
IF OBJECT_ID('dbo.vw_erp_cliente', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_cliente AS SELECT 1 AS stub');
IF OBJECT_ID('dbo.vw_erp_articulo', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_articulo AS SELECT 1 AS stub');
GO

/* Limpieza estándar del diseño JSON: sentinel '<indf>' y vacíos → NULL. */
ALTER FUNCTION dbo.fn_limpia(@s nvarchar(max))
RETURNS nvarchar(max)
AS
BEGIN
    RETURN NULLIF(NULLIF(LTRIM(RTRIM(@s)), N''), N'<indf>');
END;
GO

/* Solo letras y números (RFC, teléfono). */
ALTER FUNCTION dbo.fn_solo_alfanumerico(@s nvarchar(200))
RETURNS nvarchar(200)
AS
BEGIN
    DECLARE @r nvarchar(200) = N'', @i int = 1, @c nchar(1);
    WHILE @i <= LEN(ISNULL(@s, N''))
    BEGIN
        SET @c = SUBSTRING(@s, @i, 1);
        IF @c LIKE N'[A-Za-z0-9]' SET @r += @c;
        SET @i += 1;
    END;
    RETURN NULLIF(@r, N'');
END;
GO

/* ---------------------------------------------------------------------------
   vw_erp_pedido_cabecera — doc 04 §3.1
   -------------------------------------------------------------------------*/
ALTER VIEW dbo.vw_erp_pedido_cabecera
AS
SELECT
    CAST(K.ID AS nvarchar(50))                              AS numero_pedido,
    -- El ERP resuelve este valor contra compartido.sucursales.clave_aw
    -- (campo editable en el admin) — aquí solo se normaliza el alias.
    CASE WHEN dbo.fn_limpia(K.OR_AVBEREICH) = N'PERIFERICO'
         THEN N'CONKAL'
         ELSE dbo.fn_limpia(K.OR_AVBEREICH) END             AS numero_sucursal,
    CAST(K.AH_IDENT AS nvarchar(50))                        AS cliente_ref,
    dbo.fn_limpia(LTRIM(RTRIM(
        ISNULL(C.NAME1, N'') + N' ' + ISNULL(C.NAME2, N''))))
                                                            AS cliente_nombre,
    dbo.fn_solo_alfanumerico(C.UST_ID)                      AS rfc_cliente,
    -- VALIDAR: regla exportación (AH_KOPF='Interfaz EDI' o GRUPPE='Ventas
    -- Internacionales') → 'S01'; si no, KA_LIEFERBED.FREMD_KEY.
    -- OJO (2026-07-06): FREMD_KEY real trae valores numéricos ('1','17'...),
    -- no claves SAT 'G01' — confirmar mapeo con equipo A+W (gap G6).
    CASE WHEN dbo.fn_limpia(K.AH_KOPF) = N'Interfaz EDI'
           OR dbo.fn_limpia(M.GRUPPE) = N'Ventas Internacionales'
         THEN N'S01'
         ELSE dbo.fn_limpia(LB.FREMD_KEY) END               AS uso_cfdi,
    CASE WHEN ISNULL(dbo.fn_limpia(K.FI_ZAHLBED), N'CONTADO') = N'CONTADO'
         THEN N'PUE' ELSE N'PPD' END                        AS metodo_pago,
    ISNULL(dbo.fn_limpia(ZW.FREMD_KEY), N'99')              AS forma_pago,
    ISNULL(dbo.fn_limpia(K.FI_ZAHLBED), N'CONTADO')         AS condicion_pago,
    ISNULL(dbo.fn_limpia(W.FREMD_KEY), N'MXN')              AS divisa,
    NULLIF(K.KO_OBJEKT_KUNDE, 0)                            AS obra_id,
    CASE WHEN K.KO_OBJEKT_KUNDE = 0 THEN NULL
         ELSE dbo.fn_limpia(O.BEZ) END                      AS obra_nombre,
    -- RtfToText(@rtf nvarchar) → nvarchar, confirmada en SYSADM (ronda 2):
    -- convierte el RTF de las notas a texto plano antes de normalizar saltos.
    (SELECT TOP 1 dbo.fn_limpia(REPLACE(
                MILMAIN.SYSADM.RtfToText(T.BEZ),
                CHAR(13) + CHAR(10), CHAR(10)))
       FROM MILMAIN.SYSADM.BW_AUFTR_KTXT AS T
      WHERE T.ID = K.ID)                                    AS notas_pedido,
    -- Canal de venta (rediseño 2026-07-08): la traducción GRUPPE → canal
    -- ya NO vive en esta vista — vive en el catálogo del ERP
    -- (compartido.canales_venta.clave_aw, administrable sin redeploy,
    -- FAC-ING-PR2). La vista expone el GRUPPE crudo y solo normaliza el
    -- caso EDI: esos pedidos (~12k) llegan con OR_BEARBEITER sin GRUPPE y
    -- se alinean al GRUPPE canónico de exportación (mismo patrón alias que
    -- PERIFERICO→CONKAL en numero_sucursal). Un GRUPPE sin clave_aw en el
    -- catálogo cae a la bandeja con el valor en el detalle.
    CASE WHEN dbo.fn_limpia(K.AH_KOPF) = N'Interfaz EDI'
         THEN N'Ventas Internacionales'
         ELSE dbo.fn_limpia(M.GRUPPE) END                   AS canal_ventas,
    -- Mapeo → enum ComportamientoFiscal del ERP (gap G14 CERRADO 2026-07-08,
    -- respuesta del equipo fiscal): la señal es la CONDICIÓN DE PAGO, no el
    -- GRUPPE. Exportación → ExportacionConCce (regla fija del diseño);
    -- CONTADO → MostradorInmediato; cualquier otra condición — crédito,
    -- REPARTO (contraentrega, crédito de 24 horas) — → ConAnticipo.
    -- El default a CONTADO con FI_ZAHLBED vacío/'<indf>' es el MISMO que usa
    -- metodo_pago (PUE) arriba — mantener ambos CASE alineados.
    -- Escape hatch operativo: IntegracionesAw:Pedidos:MapeoComportamiento.
    CASE WHEN dbo.fn_limpia(K.AH_KOPF) = N'Interfaz EDI'
           OR dbo.fn_limpia(M.GRUPPE) = N'Ventas Internacionales'
         THEN N'ExportacionConCce'
         WHEN ISNULL(dbo.fn_limpia(K.FI_ZAHLBED), N'CONTADO') = N'CONTADO'
         THEN N'MostradorInmediato'
         ELSE N'ConAnticipo' END                            AS clase,
    CAST(K.DATUM_ERF AS date)                               AS fecha_transaccion,
    NULLIF(K.AH_HAUPT_AUFTR, 0)                             AS pedido_sustituido_numero,
    -- VALIDAR: columna real del estatus del pedido en A+W (gap G6).
    CAST(NULL AS nvarchar(20))                              AS estado_origen,
    -- Totales de control (validación cabecera-vs-líneas en el ERP).
    -- SEMÁNTICA (FAC-DET-PR2, info del owner 2026-07-08): los importes de
    -- A+W (importe_pieza, descuento, importe_total) son BRUTOS (con IVA).
    -- El ERP calcula el neto hacia atrás: neto = bruto / (1 + tasa).
    TOT.total_cantidad                                      AS total_cantidad,
    TOT.total_m2                                            AS total_m2,
    TOT.importe_total                                       AS importe_total,
    -- IVA a nivel documento (FAC-DET-PR2): la tasa del documento se
    -- transfiere a las posiciones; no hay IVA por posición en A+W.
    -- Se expone en PORCENTAJE (16.00 / 0.00); el reader normaliza a fracción.
    -- CONFIRMADO en SER-DATA (2026-07-08, gap G15 cerrado): KA_MWST.MWST es
    -- el porcentaje mismo (decimal, p.ej. 16.00000000; KZ_GESPERRT=1 marca
    -- tasas históricas bloqueadas). El join valida contra el catálogo.
    CAST(MW.MWST AS decimal(9, 4))                          AS iva_porcentaje,
    -- Ranura (RANURA-PR1): descuento a nivel cabecera del pedido. Mapeo del
    -- catálogo del diseño JSON (campo 32 de Encabezado, confirmado por el
    -- owner 2026-07-15): BW_AUFTR_KOPF.KO_FALZ, decimal(28,8), nullable.
    -- VALIDAR con un pedido real: que KO_FALZ es BRUTO (con IVA) como los
    -- demás importes. El ERP la documenta como NC (relación 01) — la factura
    -- va por el total y la caja cobra total − NC.
    CAST(NULLIF(K.KO_FALZ, 0) AS decimal(28, 8))            AS ranura
FROM MILMAIN.SYSADM.BW_AUFTR_KOPF   AS K
LEFT JOIN MILMAIN.SYSADM.KU_KUNDEN        AS C  ON C.ID = K.AH_IDENT
LEFT JOIN MILMAIN.SYSADM.KA_FIRMA_MITARB  AS M  ON M.ID = K.OR_BEARBEITER
LEFT JOIN MILMAIN.SYSADM.KA_OBJEKT        AS O  ON O.ID = K.KO_OBJEKT_KUNDE
-- Confirmado (ronda 2): BW_AUFTR_KOPF referencia catálogos por nombre —
-- OR_LIEFERBED nvarchar(40) trae 'CAMION'/'<indf>', FI_ZAHLWEG 'EFECTIVO'.
-- El JOIN va por BEZ; '<indf>' simplemente no cruza y deja NULL.
LEFT JOIN MILMAIN.SYSADM.KA_LIEFERBED     AS LB ON LB.BEZ = K.OR_LIEFERBED
LEFT JOIN MILMAIN.SYSADM.KA_ZAHLWEG       AS ZW ON ZW.BEZ = K.FI_ZAHLWEG
LEFT JOIN MILMAIN.SYSADM.KA_WAEHRUNGEN    AS W  ON W.WAEHRUNG   = K.FI_WAEHRUNG
-- IVA del documento (FAC-DET-PR2): join confirmado por el owner
-- (KA_MWST.MWST = BW_AUFTR_KOPF.FI_MWST1). El DISTINCT es obligatorio:
-- KA_MWST trae DOS filas activas con MWST=0.00000000 (verificado 2026-07-08,
-- Q4d del script 06) y un join directo duplicaría la cabecera de todo
-- pedido con IVA 0% (justo los de exportación).
LEFT JOIN (SELECT DISTINCT MWST
             FROM MILMAIN.SYSADM.KA_MWST) AS MW ON MW.MWST = K.FI_MWST1
OUTER APPLY (
    SELECT SUM(P.PP_MENGE)                       AS total_cantidad,
           CAST(SUM(P.PP_QM * P.PP_MENGE) AS decimal(28, 4)) AS total_m2,
           -- VALIDAR: importe total con DEVUELVE_IMPORTE_Y_DESCTO_N (perf G13);
           -- si es caro, exponer NULL y el ERP omite la validación de importes.
           CAST(NULL AS decimal(28, 4))          AS importe_total
      FROM MILMAIN.SYSADM.BW_AUFTR_POS AS P
     WHERE P.ID = K.ID
) AS TOT;
GO

/* ---------------------------------------------------------------------------
   vw_erp_pedido_linea — doc 04 §3.2
   -------------------------------------------------------------------------*/
ALTER VIEW dbo.vw_erp_pedido_linea
AS
SELECT
    CAST(P.ID AS nvarchar(50))                              AS numero_pedido,
    P.POS_NR                                                AS numero_posicion,
    CAST(P.PROD_ID AS nvarchar(50))                         AS producto_ref,
    dbo.fn_limpia(P.PROD_BEZ1)                              AS descripcion,
    -- VALIDAR: concatenación de procesos (BW_AUFTR_STKL.STL_BEZ, filtro
    -- BOM_BASE_ID<>1, BOM_MASTER_ID<>8, BOM_LEVEL=1). Informativo → snapshot.
    -- FOR XML PATH en lugar de STRING_AGG (no existe pre-SQL 2017).
    STUFF((SELECT N' / ' + dbo.fn_limpia(S.STL_BEZ)
             FROM MILMAIN.SYSADM.BW_AUFTR_STKL AS S
            WHERE S.ID = P.ID AND S.POS_NR = P.POS_NR
              AND S.BOM_BASE_ID <> 1 AND S.BOM_MASTER_ID <> 8
              AND S.BOM_LEVEL = 1
              FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'),
          1, 3, N'')                                        AS detalle_procesos,
    P.PP_MENGE                                              AS cantidad,
    UPPER(REPLACE(ISNULL(P.PR_EINHEIT, N''), N'²', N'2'))   AS unidad_medida,
    -- Firma real (confirmada contra MILMAIN 2026-07-06):
    -- DEVUELVE_IMPORTE_Y_DESCTO_N(@PEDIDO, @POS, @FAMILIA, @ARTICULO,
    --                             @MONEDA, @AH_HAUPT_AUFTR, @TIPO)
    -- @FAMILIA = BA_PRODUKTE.BA_WGR; @ARTICULO = descripción (detecta 'H.E.');
    -- @MONEDA = nombre A+W crudo (compara 'PESOSMX'); se pasa por fn_limpia
    -- porque FI_WAEHRUNG puede traer '<indf>' y sin limpiar tomaría la rama
    -- de moneda extranjera. @TIPO = 'P' importe por pieza / 'D' descuento %.
    -- Devuelve en la moneda del pedido.
    CAST(MILMAIN.SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N(
             P.ID, P.POS_NR, BP.BA_WGR, P.PROD_BEZ1,
             ISNULL(dbo.fn_limpia(K.FI_WAEHRUNG), N''), K.AH_HAUPT_AUFTR, 'P')
         AS decimal(28, 8))                                 AS importe_pieza,
    CAST(MILMAIN.SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N(
             P.ID, P.POS_NR, BP.BA_WGR, P.PROD_BEZ1,
             ISNULL(dbo.fn_limpia(K.FI_WAEHRUNG), N''), K.AH_HAUPT_AUFTR, 'D')
         AS decimal(28, 8))                                 AS descuento_porcentaje,
    CAST(MILMAIN.SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N(
             P.ID, P.POS_NR, BP.BA_WGR, P.PROD_BEZ1,
             ISNULL(dbo.fn_limpia(K.FI_WAEHRUNG), N''), K.AH_HAUPT_AUFTR, 'P')
         * P.PP_MENGE
         * MILMAIN.SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N(
             P.ID, P.POS_NR, BP.BA_WGR, P.PROD_BEZ1,
             ISNULL(dbo.fn_limpia(K.FI_WAEHRUNG), N''), K.AH_HAUPT_AUFTR, 'D')
         / 100.0 AS decimal(28, 4))                         AS descuento,
    dbo.fn_limpia(D.LEVEL1)                                 AS almacen_nivel_1,
    dbo.fn_limpia(D.LEVEL2)                                 AS almacen_nivel_2,
    dbo.fn_limpia(D.LEVEL3)                                 AS almacen_nivel_3,
    dbo.fn_limpia(D.LEVEL4)                                 AS almacen_nivel_4,
    P.PROD_LAGERORT                                         AS almacen_id_ubicacion,
    -- Gap G5: origen del pedimento por posición sin definir en A+W.
    CAST(NULL AS bit)                                       AS requiere_pedimento
FROM MILMAIN.SYSADM.BW_AUFTR_POS AS P
JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF AS K ON K.ID = P.ID
LEFT JOIN MILMAIN.SYSADM.BA_PRODUKTE  AS BP ON BP.BA_PRODUKT = P.PROD_ID
LEFT JOIN MILMAIN.SYSADM.KA_LAGER_DEF AS D  ON D.ID = P.PROD_LAGERORT;
GO

/* ---------------------------------------------------------------------------
   vw_erp_pedido_componente — doc 04 §3.3 (BOM → bom_json por línea)
   -------------------------------------------------------------------------*/
ALTER VIEW dbo.vw_erp_pedido_componente
AS
SELECT
    CAST(S.ID AS nvarchar(50))                              AS numero_pedido,
    S.POS_NR                                                AS numero_posicion,
    CAST(S.BOM_PRODUKT AS nvarchar(50))                     AS producto_ref,
    dbo.fn_limpia(S.STL_BEZ)                                AS descripcion,
    S.STL_HOEHE                                             AS alto_mm,
    S.STL_BREITE                                            AS ancho_mm,
    S.STL_QM                                                AS m2_por_pieza,
    -- VALIDAR: moneda del pedido decide PR_BETR_NETTO vs PR_BETR_NETTO_FW
    -- (diseño JSON v2 §2.2 — JOIN con BW_AUFTR_KOPF + KA_WAEHRUNGEN).
    CASE WHEN ISNULL(dbo.fn_limpia(W.FREMD_KEY), N'MXN') = N'MXN'
         THEN S.PR_BETR_NETTO
         ELSE S.PR_BETR_NETTO_FW END                        AS importe
FROM MILMAIN.SYSADM.BW_AUFTR_STKL AS S
JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF AS K ON K.ID = S.ID
LEFT JOIN MILMAIN.SYSADM.KA_WAEHRUNGEN AS W ON W.WAEHRUNG = K.FI_WAEHRUNG
WHERE S.PREISRELEVANT = 1
  AND S.STL_PRODART IN (1, 2, 3, 30, 50, 60);
GO

/* ---------------------------------------------------------------------------
   vw_erp_cliente — doc 04 §3.4 (auto-provisión)
   -------------------------------------------------------------------------*/
ALTER VIEW dbo.vw_erp_cliente
AS
SELECT
    CAST(C.ID AS nvarchar(50))                              AS cliente_ref,
    dbo.fn_limpia(LTRIM(RTRIM(
        ISNULL(C.NAME1, N'') + N' ' + ISNULL(C.NAME2, N''))))
                                                            AS razon_social,
    dbo.fn_solo_alfanumerico(C.UST_ID)                      AS rfc,
    -- VALIDAR: columnas reales de domicilio en KU_KUNDEN (gap G12; en
    -- BW_AUFTR_KOPF el patrón es NAME3=calle, STRASSE=colonia, PLZ=CP...).
    dbo.fn_limpia(C.NAME3)                                  AS calle,
    dbo.fn_limpia(C.STRASSE)                                AS colonia,
    dbo.fn_limpia(C.PLZ)                                    AS cp,
    dbo.fn_limpia(C.ORT)                                    AS ciudad,
    dbo.fn_limpia(C.PROVINZ)                                AS estado,
    dbo.fn_limpia(C.LAND)                                   AS pais,
    -- Columna real de teléfono en KU_KUNDEN = TLF1 (hay TLF2 secundario).
    dbo.fn_solo_alfanumerico(C.TLF1)                        AS telefono
FROM MILMAIN.SYSADM.KU_KUNDEN AS C;
GO

/* ---------------------------------------------------------------------------
   vw_erp_articulo — doc 04 §3.5 (auto-provisión)
   -------------------------------------------------------------------------*/
ALTER VIEW dbo.vw_erp_articulo
AS
-- Contrato congelado: producto_ref, descripcion, unidad_medida.
-- Descripción/unidad viven en BA_PRODUKTE_BEZ (confirmado ronda 3):
-- SPRACH_ID 0 = español ('2MM CLARO'), 1 = inglés ('5/64" CLEAR');
-- BA_MENGENEINH = unidad ('Pza', 'm²'). Fallback a BA_MCODE si el
-- producto no tiene fila de textos.
SELECT
    CAST(PR.BA_PRODUKT AS nvarchar(50))                     AS producto_ref,
    COALESCE(dbo.fn_limpia(BZ.BA_BEZ1),
             dbo.fn_limpia(PR.BA_MCODE))                    AS descripcion,
    UPPER(REPLACE(ISNULL(BZ.BA_MENGENEINH, N''), N'²', N'2'))
                                                            AS unidad_medida
FROM MILMAIN.SYSADM.BA_PRODUKTE AS PR
LEFT JOIN MILMAIN.SYSADM.BA_PRODUKTE_BEZ AS BZ
       ON BZ.BA_PRODUKT = PR.BA_PRODUKT AND BZ.SPRACH_ID = 0;
GO

/* Rollback:
   -- DROP VIEW dbo.vw_erp_pedido_cabecera, dbo.vw_erp_pedido_linea,
   --           dbo.vw_erp_pedido_componente, dbo.vw_erp_cliente,
   --           dbo.vw_erp_articulo;
   -- DROP FUNCTION dbo.fn_limpia, dbo.fn_solo_alfanumerico;
*/
