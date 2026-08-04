/* =============================================================================
   06_consultas_seed_inicial.sql — Diagnóstico de catálogos para el seed
   previo a la ingesta de pedidos (ADR-0048, doc integration/04).

   Ejecutar en: MILLET_INTEGRACION_DEV (usa dbo.fn_limpia del 03; solo lectura
   sobre MILMAIN). Compatible con SQL Server 2016 RTM (sin STRING_AGG).

   Cada bloque indica QUÉ SEED del ERP alimenta su resultado:

   Q1 → compartido.sucursales.clave_aw  (alta/edición en /admin/empresas)
   Q2 → CASE canal_ventas/clase de 03_create_views.sql
        (u overrides IntegracionesAw:Pedidos:MapeoCanalVenta / MapeoComportamiento)
   Q3 → IntegracionesAw:Pedidos:MapeoUnidadSat
   Q4 → catálogos fiscales A+W (uso_cfdi/forma_pago/divisa/IVA — gaps G6/G15)
   Q5 → placeholders <<PEDIDO_REAL_N>> de 05_seed_dev.sql
   Q6 → pre-check: pedidos recientes que HOY caerían a la bandeja
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;
GO

/* ---------------------------------------------------------------------------
   Q1. Sucursales (clave_aw) usadas en pedidos — últimos 24 meses.
   El valor que sale aquí (ya con el alias PERIFERICO→CONKAL de la vista) es
   EXACTAMENTE lo que hay que capturar en compartido.sucursales.clave_aw,
   con la sucursal en estatus Activo. Mayúsculas/minúsculas no importan
   (el resolver compara case-insensitive), espacios sí (fn_limpia los quita).
   -------------------------------------------------------------------------*/
SELECT
    CASE WHEN dbo.fn_limpia(K.OR_AVBEREICH) = N'PERIFERICO'
         THEN N'CONKAL'
         ELSE dbo.fn_limpia(K.OR_AVBEREICH) END       AS clave_aw,
    COUNT(*)                                          AS pedidos,
    MIN(CAST(K.DATUM_ERF AS date))                    AS primer_pedido,
    MAX(CAST(K.DATUM_ERF AS date))                    AS ultimo_pedido
FROM MILMAIN.SYSADM.BW_AUFTR_KOPF AS K
WHERE K.DATUM_ERF >= DATEADD(MONTH, -24, GETDATE())
GROUP BY CASE WHEN dbo.fn_limpia(K.OR_AVBEREICH) = N'PERIFERICO'
              THEN N'CONKAL'
              ELSE dbo.fn_limpia(K.OR_AVBEREICH) END
ORDER BY pedidos DESC;
GO

/* ---------------------------------------------------------------------------
   Q2. "Tipo de negocio" real: GRUPPE del vendedor (KA_FIRMA_MITARB via
   OR_BEARBEITER) + la marca de exportación AH_KOPF. Con esto se completan:
     - el CASE canal_ventas de la vista (gap G6): cada GRUPPE debe traducirse
       a un nombre del enum CanalVenta (TiendaCancun, TiendaCircuito,
       TiendaChichiSuarez, CcMid, CcQRoo, CcMpios, ProyectosYObras,
       Exportacion, PlantaPintura, Administracion);
     - la regla clase → ComportamientoFiscal (gap G14): MostradorInmediato,
       ConAnticipo, ExportacionConCce, TrasladoConCartaPorte,
       VentaActivoFijo, Administrativa.
   Todo GRUPPE sin rama en el CASE pasa crudo y cae a la bandeja.
   -------------------------------------------------------------------------*/
SELECT
    dbo.fn_limpia(M.GRUPPE)                           AS gruppe,
    SUM(CASE WHEN dbo.fn_limpia(K.AH_KOPF) = N'Interfaz EDI'
             THEN 1 ELSE 0 END)                       AS pedidos_interfaz_edi,
    COUNT(*)                                          AS pedidos,
    MAX(CAST(K.DATUM_ERF AS date))                    AS ultimo_pedido
FROM MILMAIN.SYSADM.BW_AUFTR_KOPF AS K
LEFT JOIN MILMAIN.SYSADM.KA_FIRMA_MITARB AS M ON M.ID = K.OR_BEARBEITER
WHERE K.DATUM_ERF >= DATEADD(MONTH, -24, GETDATE())
GROUP BY dbo.fn_limpia(M.GRUPPE)
ORDER BY pedidos DESC;
GO

/* ---------------------------------------------------------------------------
   Q3. Unidades de medida — para completar MapeoUnidadSat (hoy: M2→MTK,
   PZA→H87, KG→KGM; ML pendiente). Dos ángulos: catálogo de artículos y
   lo realmente usado en posiciones de pedido.
   -------------------------------------------------------------------------*/
-- 3a. Unidades del catálogo de artículos (mismo cálculo que vw_erp_articulo)
SELECT
    UPPER(REPLACE(ISNULL(BZ.BA_MENGENEINH, N''), N'²', N'2')) AS unidad_medida,
    COUNT(*)                                                  AS articulos
FROM MILMAIN.SYSADM.BA_PRODUKTE AS PR
LEFT JOIN MILMAIN.SYSADM.BA_PRODUKTE_BEZ AS BZ
       ON BZ.BA_PRODUKT = PR.BA_PRODUKT AND BZ.SPRACH_ID = 0
GROUP BY UPPER(REPLACE(ISNULL(BZ.BA_MENGENEINH, N''), N'²', N'2'))
ORDER BY articulos DESC;

-- 3b. Unidades usadas en líneas de pedidos recientes (vw_erp_pedido_linea)
SELECT
    UPPER(REPLACE(ISNULL(P.PR_EINHEIT, N''), N'²', N'2'))     AS unidad_medida,
    COUNT(*)                                                  AS lineas
FROM MILMAIN.SYSADM.BW_AUFTR_POS  AS P
JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF AS K ON K.ID = P.ID
WHERE K.DATUM_ERF >= DATEADD(MONTH, -24, GETDATE())
GROUP BY UPPER(REPLACE(ISNULL(P.PR_EINHEIT, N''), N'²', N'2'))
ORDER BY lineas DESC;
GO

/* ---------------------------------------------------------------------------
   Q4. Catálogos fiscales de A+W tal cual los lee la vista de cabecera.
   Sirven para validar los gaps G6 (FREMD_KEY numérico vs clave SAT) y para
   saber qué valores llegarán en uso_cfdi / forma_pago / divisa / IVA.
   -------------------------------------------------------------------------*/
-- 4a. Forma de pago (KA_ZAHLWEG → forma_pago; fallback '99')
SELECT ZW.BEZ, ZW.FREMD_KEY FROM MILMAIN.SYSADM.KA_ZAHLWEG AS ZW ORDER BY ZW.BEZ;

-- 4b. Uso CFDI (KA_LIEFERBED → uso_cfdi; OJO G6: FREMD_KEY trae numéricos)
SELECT LB.BEZ, LB.FREMD_KEY FROM MILMAIN.SYSADM.KA_LIEFERBED AS LB ORDER BY LB.BEZ;

-- 4c. Monedas (KA_WAEHRUNGEN → divisa; fallback 'MXN')
SELECT W.WAEHRUNG, W.FREMD_KEY FROM MILMAIN.SYSADM.KA_WAEHRUNGEN AS W ORDER BY W.WAEHRUNG;

-- 4d. Tasas de IVA (KA_MWST; KZ_GESPERRT=1 = tasa histórica bloqueada)
SELECT MW.MWST, MW.KZ_GESPERRT FROM MILMAIN.SYSADM.KA_MWST AS MW ORDER BY MW.MWST;

-- 4e. Condiciones de pago usadas (FI_ZAHLBED → condicion_pago y regla PUE/PPD)
SELECT
    ISNULL(dbo.fn_limpia(K.FI_ZAHLBED), N'CONTADO')   AS condicion_pago,
    COUNT(*)                                          AS pedidos
FROM MILMAIN.SYSADM.BW_AUFTR_KOPF AS K
WHERE K.DATUM_ERF >= DATEADD(MONTH, -24, GETDATE())
GROUP BY ISNULL(dbo.fn_limpia(K.FI_ZAHLBED), N'CONTADO')
ORDER BY pedidos DESC;
GO

/* ---------------------------------------------------------------------------
   Q5. Candidatos para los placeholders <<PEDIDO_REAL_N>> de 05_seed_dev.sql.
   Va directo a tablas base (la vista de cabecera es cara por RtfToText +
   OUTER APPLY). Elegir 5 variados: mostrador MXN, exportación, con obra,
   multi-línea, con descuento.
   -------------------------------------------------------------------------*/
SELECT TOP 30
    CAST(K.ID AS nvarchar(50))                        AS numero_pedido,
    CAST(K.DATUM_ERF AS date)                         AS fecha,
    CASE WHEN dbo.fn_limpia(K.OR_AVBEREICH) = N'PERIFERICO'
         THEN N'CONKAL'
         ELSE dbo.fn_limpia(K.OR_AVBEREICH) END       AS sucursal,
    dbo.fn_limpia(M.GRUPPE)                           AS gruppe,
    dbo.fn_limpia(K.AH_KOPF)                          AS ah_kopf,
    dbo.fn_limpia(K.FI_WAEHRUNG)                      AS moneda,
    ISNULL(dbo.fn_limpia(K.FI_ZAHLBED), N'CONTADO')   AS condicion_pago,
    NULLIF(K.KO_OBJEKT_KUNDE, 0)                      AS obra_id,
    (SELECT COUNT(*) FROM MILMAIN.SYSADM.BW_AUFTR_POS AS P
      WHERE P.ID = K.ID)                              AS lineas
FROM MILMAIN.SYSADM.BW_AUFTR_KOPF AS K
LEFT JOIN MILMAIN.SYSADM.KA_FIRMA_MITARB AS M ON M.ID = K.OR_BEARBEITER
WHERE EXISTS (SELECT 1 FROM MILMAIN.SYSADM.BW_AUFTR_POS AS P WHERE P.ID = K.ID)
ORDER BY K.DATUM_ERF DESC;
GO

/* Verificación puntual de un candidato contra las 3 vistas (reemplazar 12345):
   SELECT * FROM dbo.vw_erp_pedido_cabecera  WHERE numero_pedido = N'12345';
   SELECT * FROM dbo.vw_erp_pedido_linea     WHERE numero_pedido = N'12345';
   SELECT * FROM dbo.vw_erp_pedido_componente WHERE numero_pedido = N'12345';
   SELECT * FROM dbo.vw_erp_cliente  WHERE cliente_ref  =
       (SELECT cliente_ref FROM dbo.vw_erp_pedido_cabecera WHERE numero_pedido = N'12345');
*/

/* ---------------------------------------------------------------------------
   Q6. Pre-check de bandeja: pedidos de los últimos 3 meses cuyo canal/clase
   NO parsearía a los enums del ERP con el CASE actual de la vista (es decir,
   GRUPPE distinto de los ya mapeados y sin marca de exportación). Todo lo
   que salga aquí necesita rama nueva en el CASE u override en app settings
   ANTES de sembrar solicitudes de esos pedidos.
   -------------------------------------------------------------------------*/
SELECT
    dbo.fn_limpia(M.GRUPPE)                           AS gruppe_sin_mapeo,
    COUNT(*)                                          AS pedidos_3m
FROM MILMAIN.SYSADM.BW_AUFTR_KOPF AS K
LEFT JOIN MILMAIN.SYSADM.KA_FIRMA_MITARB AS M ON M.ID = K.OR_BEARBEITER
WHERE K.DATUM_ERF >= DATEADD(MONTH, -3, GETDATE())
  AND ISNULL(dbo.fn_limpia(K.AH_KOPF), N'') <> N'Interfaz EDI'
  AND ISNULL(dbo.fn_limpia(M.GRUPPE), N'(NULL)') NOT IN
      (N'Ventas Cancun', N'Ventas Internacionales',
       -- nombres del enum por si algún GRUPPE ya coincide literal:
       N'TiendaCancun', N'TiendaCircuito', N'TiendaChichiSuarez',
       N'CcMid', N'CcQRoo', N'CcMpios', N'ProyectosYObras',
       N'Exportacion', N'PlantaPintura', N'Administracion')
GROUP BY dbo.fn_limpia(M.GRUPPE)
ORDER BY pedidos_3m DESC;
GO
