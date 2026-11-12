/* =============================================================================
   22_grant_lector_expedicion_parcial.sql — SELECT del lector OTR sobre las
   posiciones de pedido (MILMAIN.SYSADM.BW_AUFTR_POS)
   Ejecutar como: sysadmin o db_owner de MILMAIN. Idempotente.
   PROPUESTA: no se ejecuta sin visto bueno de TI (Jorge Toache).

   Por qué (diagnóstico OTR del pedido 10446260, 12-nov): las piezas de OTR se
   etiquetan con el pedido de origen aunque salgan con el albarán de un pedido
   hijo «Expedición parcial» (TI, 11-nov, hilo «Estatus 71, 72 y 115»). Para
   repartir las piezas entre el hijo y el origen, el lector OTR del ERP
   (Exportacion/OtrTablasAdapter, conexión AwClientesDb = millet_erp_lector_aw)
   lee de BW_AUFTR_POS sólo estas columnas, por pedido de origen:
     ID, POS_NR, PP_MENGE, REF_AUSLIEF_AUFTR_ID, REF_AUSLIEF_AUFTR_POS
   junto con BW_AUFTR_KOPF (DOK_TYP, OR_AVBEREICH, STATUS), que ya tiene
   (script 09). Excluye OR_AVBEREICH = 'RECLAMACIONES'.

   Sin este permiso el lector sigue funcionando como hoy: todo se propone
   contra el pedido de origen y el tope K3 del ERP deja pendiente lo que exceda.

   Qué otorga: SELECT sobre una tabla de MILMAIN. Nada de escritura.
   ========================================================================== */

USE MILMAIN;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_lector_aw') IS NOT NULL
    GRANT SELECT ON SYSADM.BW_AUFTR_POS TO millet_erp_lector_aw;
ELSE
    PRINT 'Falta el usuario millet_erp_lector_aw en MILMAIN (script 09).';
GO

/* Comprobación (como millet_erp_lector_aw): hijos del pedido 10446260.
   Esperado (12-nov): un hijo 11022579, 36 posiciones, 105 piezas.
   SELECT K.ID, COUNT(*) AS posiciones, SUM(P.PP_MENGE) AS piezas
     FROM MILMAIN.SYSADM.BW_AUFTR_POS P
     JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF K ON K.ID = P.ID
    WHERE TRY_CONVERT(int, P.REF_AUSLIEF_AUFTR_ID) = 10446260 AND P.ID <> 10446260
      AND K.DOK_TYP = N'Expedici' + NCHAR(243) + N'n parcial'
      AND ISNULL(K.OR_AVBEREICH, N'') <> N'RECLAMACIONES'
    GROUP BY K.ID;
*/

/* Rollback:
   USE MILMAIN; REVOKE SELECT ON SYSADM.BW_AUFTR_POS FROM millet_erp_lector_aw;
*/
