/* =============================================================================
   20_create_vista_albaran.sql — Vista de albaranes de A+W por posición para
   descontar producto terminado (PT) en el ERP.
   Ejecutar en: MILLET_INTEGRACION_DEV primero; MILLET_INTEGRACION al arranque
   (cambiar USE). Idempotente (stub + ALTER, como 03: SQL Server 2016 RTM no
   tiene CREATE OR ALTER). Versión 2 (11-nov): visto bueno de TI (Jorge
   Toache, 11-nov 10:26) con tres ajustes, ya aplicados aquí:
     1) piezas: la resta PP_ORIG_MENGE - PP_TEILGEL_MENGE solo con factura
        parcial (KO_TEILFAK = 1); con factura agrupada A+W no descuenta del
        pedido original y su albarán cubre PP_MENGE completo.
     2) el remapeo al pedido de origen excluye el departamento RECLAMACIONES
        (OR_AVBEREICH): ahí hay pedidos con DOK_TYP de expedición parcial.
     3) DOK_TYP se compara contra el catálogo KA_DOK_TYP.BEZ, armando el texto
        con NCHAR(243) (ó): el archivo no depende de la página de códigos.

   Decisión (Geovany, 10-nov): el albarán se queda en A+W; el ERP solo emite la
   hoja de salida. TI (Jorge Toache, 10-nov): en A+W el albarán no es un
   documento, es un dato de la cabecera del pedido:
     - SYSADM.BW_AUFTR_KOPF.NR_LIEFERSCHEIN (int)  = folio del albarán
     - SYSADM.BW_AUFTR_KOPF.DATUM_LIEFERSCHEIN (date) = fecha del albarán
   Pedido, posición y piezas vienen de SYSADM.BW_AUFTR_POS (join por pedido).

   Entregas parciales (revisado en la copia local de MILMAIN, corte 2-oct):
   - BW_AUFTR_POS NO tiene columnas de albarán. La cabecera guarda UN solo folio.
   - Una entrega parcial en A+W crea un pedido hijo con DOK_TYP = 'Expedición
     parcial' y su propio NR_LIEFERSCHEIN. Sus posiciones apuntan al pedido y
     posición de origen con REF_AUSLIEF_AUFTR_ID / REF_AUSLIEF_AUFTR_POS
     (nvarchar en A+W). El pedido de origen acumula lo entregado en
     PP_TEILGEL_MENGE y su propio albarán cubre el resto (PP_ORIG_MENGE -
     PP_TEILGEL_MENGE). Ejemplo: 80269432 pos. 1 (111 piezas) salió en 4
     albaranes: 27 + 28 + 28 por pedidos hijo y 28 con el del pedido.
   - Las reclamaciones son piezas nuevas con su propio número de pedido: se
     quedan con su pedido y posición y no descuentan del pedido original. En
     A+W una reclamación se reconoce por el departamento OR_AVBEREICH =
     'RECLAMACIONES' (no solo por DOK_TYP) y su origen va en AH_HAUPT_AUFTR,
     no en REF_AUSLIEF_* (TI, 11-nov). Por eso el remapeo las excluye.
   - Por eso la vista da una fila por (albarán, posición) y la columna
     numero_pedido / numero_posicion es la del pedido donde se produjeron las
     piezas (lote de PT = pedido-posición). El ERP suma todos los albaranes de
     esa posición.

   Limitaciones que se aceptan:
   - TI confirmó (11-nov) que un folio de albarán emitido no se cambia ni se
     borra; por eso el ERP no necesita revertir salidas leídas.
   - Pedidos cancelados (STATUS >= 900) se excluyen aunque traigan folio.
   - Las piezas salen de PP_MENGE / PP_ORIG_MENGE (cantidad de la posición);
     no hay detalle pieza por pieza (etiqueta) del albarán en A+W.

   Permisos: millet_erp_integracion ya tiene SELECT sobre BW_AUFTR_KOPF y
   BW_AUFTR_POS en MILMAIN (script 04; el ownership chaining cross-database
   está apagado). Hace falta SELECT sobre la vista nueva y sobre
   SYSADM.KA_DOK_TYP (catálogo de tipos de documento, solo lectura).
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;  -- producción: MILLET_INTEGRACION
GO

IF OBJECT_ID('dbo.vw_erp_albaran_pos', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_albaran_pos AS SELECT 1 AS stub');
GO

/* Contrato de columnas (el ERP selecciona por nombre):
   numero_pedido     nvarchar(50)  pedido donde se produjeron las piezas (origen de la expedición parcial)
   numero_posicion   int           posición en ese pedido
   pedido_albaran    nvarchar(50)  pedido de A+W que lleva el albarán (el mismo o el hijo)
   posicion_albaran  int           posición en ese pedido
   piezas            decimal(28,8) piezas que salieron con este albarán
   albaran_folio     int           BW_AUFTR_KOPF.NR_LIEFERSCHEIN
   albaran_fecha     date          BW_AUFTR_KOPF.DATUM_LIEFERSCHEIN
   estatus_pedido    int           BW_AUFTR_KOPF.STATUS del pedido que lleva el albarán */
ALTER VIEW dbo.vw_erp_albaran_pos
AS
SELECT
    CAST(CASE WHEN R.remapea = 1 THEN TRY_CONVERT(int, P.REF_AUSLIEF_AUFTR_ID) ELSE P.ID END AS nvarchar(50)) AS numero_pedido,
    CASE WHEN R.remapea = 1 THEN ISNULL(TRY_CONVERT(int, P.REF_AUSLIEF_AUFTR_POS), P.POS_NR) ELSE P.POS_NR END AS numero_posicion,
    CAST(P.ID AS nvarchar(50))                                     AS pedido_albaran,
    P.POS_NR                                                       AS posicion_albaran,
    -- Con factura parcial, el albarán del pedido de origen cubre solo el resto; con factura agrupada, PP_MENGE completo.
    CAST(CASE WHEN K.KO_TEILFAK = 1 AND ISNULL(P.PP_TEILGEL_MENGE, 0) > 0
              THEN ISNULL(P.PP_ORIG_MENGE, 0) - P.PP_TEILGEL_MENGE
              ELSE P.PP_MENGE END AS decimal(28, 8))               AS piezas,
    K.NR_LIEFERSCHEIN                                              AS albaran_folio,
    K.DATUM_LIEFERSCHEIN                                           AS albaran_fecha,
    K.STATUS                                                       AS estatus_pedido
FROM MILMAIN.SYSADM.BW_AUFTR_POS AS P
JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF AS K ON K.ID = P.ID
-- Tipo «Expedición parcial» tomado del catálogo (ó = NCHAR(243)); fuera del departamento RECLAMACIONES.
LEFT JOIN MILMAIN.SYSADM.KA_DOK_TYP AS T
       ON T.BEZ = K.DOK_TYP AND T.BEZ = N'Expedici' + NCHAR(243) + N'n parcial'
CROSS APPLY (SELECT CASE WHEN T.BEZ IS NOT NULL
                          AND ISNULL(K.OR_AVBEREICH, N'') <> N'RECLAMACIONES'
                          AND TRY_CONVERT(int, P.REF_AUSLIEF_AUFTR_ID) > 0
                         THEN 1 ELSE 0 END AS remapea) AS R
WHERE K.NR_LIEFERSCHEIN IS NOT NULL AND K.NR_LIEFERSCHEIN <> 0
  AND K.STATUS < 900;
GO

IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NOT NULL
    GRANT SELECT ON dbo.vw_erp_albaran_pos TO millet_erp_integracion;
GO

-- Catálogo de tipos de documento en MILMAIN (la vista lo lee con el permiso del usuario).
USE MILMAIN;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NOT NULL
    GRANT SELECT ON SYSADM.KA_DOK_TYP TO millet_erp_integracion;
GO

/* Comprobación (como millet_erp_integracion): albaranes de la última semana.
   SELECT TOP (50) * FROM dbo.vw_erp_albaran_pos
    WHERE albaran_fecha >= DATEADD(day, -7, CAST(GETDATE() AS date))
    ORDER BY albaran_fecha, numero_pedido, numero_posicion;
   Caso con entregas parciales: WHERE numero_pedido = N'80269432' (4 albaranes:
   80119395..80119397 de los hijos y 80119398 del pedido; 27 + 28 + 28 + 28).
   El remapeo funciona (TI, 11-nov): debe dar más de cero.
   SELECT COUNT(*) FROM dbo.vw_erp_albaran_pos WHERE pedido_albaran <> numero_pedido;
   Ninguna fila con piezas negativas (antes 10409002/16 y 10322455/1):
   SELECT COUNT(*) FROM dbo.vw_erp_albaran_pos WHERE piezas < 0;
*/

/* Rollback:
   DROP VIEW dbo.vw_erp_albaran_pos;
*/
