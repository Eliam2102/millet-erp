/* =============================================================================
   04_create_login.sql — Login del ERP para la integración de pedidos (ADR-0048)
   Ejecutar como: sysadmin. Idempotente.

   Principio de mínimo privilegio:
   - millet_erp_integracion:
       · MILLET_INTEGRACION(_DEV): SELECT + UPDATE SOLO de columnas write-back
         sobre aw_solicitud_pedido; SELECT sobre las vistas.
       · MILMAIN: SELECT sobre las tablas fuente de las vistas (el
         ownership chaining cross-database está OFF por default — sin esto
         las vistas fallan con permission denied).
   - El login de la customización A+W (INSERT) lo administra el equipo A+W
     (gap G8 — solo necesita INSERT sobre aw_solicitud_pedido en PROD).

   ⚠️ La contraseña NO va en este script ni en el repo: se pasa por SQLCMD var
   o se edita en el momento (patrón carga-SAP). Después de crearla → secreto
   KV 'aw-integracion-connection-string'.
   ========================================================================== */

-- :setvar Password "REEMPLAZAR_EN_EJECUCION"
IF SUSER_ID('millet_erp_integracion') IS NULL
BEGIN
    CREATE LOGIN millet_erp_integracion
        WITH PASSWORD = N'$(Password)',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = MILLET_INTEGRACION;
    PRINT 'Login millet_erp_integracion creado.';
END
ELSE
    PRINT 'Login millet_erp_integracion ya existe — sin cambios.';
GO

/* -------- MILLET_INTEGRACION (repetir bloque en MILLET_INTEGRACION_DEV) --- */
USE MILLET_INTEGRACION;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NULL
    CREATE USER millet_erp_integracion FOR LOGIN millet_erp_integracion;
GO
GRANT SELECT ON dbo.aw_solicitud_pedido TO millet_erp_integracion;
/* UPDATE restringido a las columnas write-back del ERP: */
GRANT UPDATE (erp_pedido_id, estado_facturacion, [uuid], resultado, motivo, procesada_at)
    ON dbo.aw_solicitud_pedido TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_pedido_cabecera   TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_pedido_linea      TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_pedido_componente TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_cliente           TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_articulo          TO millet_erp_integracion;
GO

USE MILLET_INTEGRACION_DEV;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NULL
    CREATE USER millet_erp_integracion FOR LOGIN millet_erp_integracion;
GO
GRANT SELECT ON dbo.aw_solicitud_pedido TO millet_erp_integracion;
GRANT UPDATE (erp_pedido_id, estado_facturacion, [uuid], resultado, motivo, procesada_at)
    ON dbo.aw_solicitud_pedido TO millet_erp_integracion;
/* En DEV el ERP también inserta (seeds de prueba, 05_seed_dev.sql): */
GRANT INSERT, DELETE ON dbo.aw_solicitud_pedido TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_pedido_cabecera   TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_pedido_linea      TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_pedido_componente TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_cliente           TO millet_erp_integracion;
GRANT SELECT ON dbo.vw_erp_articulo          TO millet_erp_integracion;
GO

/* -------- MILMAIN: SELECT read-only sobre las fuentes de las vistas ------- */
USE MILMAIN;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NULL
    CREATE USER millet_erp_integracion FOR LOGIN millet_erp_integracion;
GO
GRANT SELECT ON SYSADM.BW_AUFTR_KOPF   TO millet_erp_integracion;
GRANT SELECT ON SYSADM.BW_AUFTR_POS    TO millet_erp_integracion;
GRANT SELECT ON SYSADM.BW_AUFTR_STKL   TO millet_erp_integracion;
GRANT SELECT ON SYSADM.BW_AUFTR_KTXT   TO millet_erp_integracion;
GRANT SELECT ON SYSADM.KU_KUNDEN       TO millet_erp_integracion;
GRANT SELECT ON SYSADM.KA_FIRMA_MITARB TO millet_erp_integracion;
GRANT SELECT ON SYSADM.KA_OBJEKT       TO millet_erp_integracion;
GRANT SELECT ON SYSADM.KA_LIEFERBED    TO millet_erp_integracion;
GRANT SELECT ON SYSADM.KA_ZAHLWEG      TO millet_erp_integracion;
GRANT SELECT ON SYSADM.KA_WAEHRUNGEN   TO millet_erp_integracion;
/* IVA de documento (FAC-DET-PR2): join KA_MWST en vw_erp_pedido_cabecera.
   Faltó al agregar el join — incidente 2026-07-08 (#229 permission denied). */
GRANT SELECT ON SYSADM.KA_MWST         TO millet_erp_integracion;
GRANT SELECT ON SYSADM.KA_LAGER_DEF    TO millet_erp_integracion;
GRANT SELECT ON SYSADM.BA_PRODUKTE     TO millet_erp_integracion;
GRANT SELECT ON SYSADM.BA_PRODUKTE_BEZ TO millet_erp_integracion;
/* Funciones SYSADM usadas por las vistas (confirmadas 2026-07-06): */
GRANT EXECUTE ON SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N TO millet_erp_integracion;
GRANT EXECUTE ON SYSADM.RtfToText                   TO millet_erp_integracion;
GO

/* Rollback:
   -- (en cada BD) DROP USER millet_erp_integracion;
   -- DROP LOGIN millet_erp_integracion;
*/
