/* =============================================================================
   09_create_login_lector.sql — Login de SOLO LECTURA para los lectores de
   clientes y productos de A+W (módulo Millet.Integraciones.Aw)
   Ejecutar como: sysadmin, en SER-DATA\AWBUSINESS. Idempotente.

   Permisos que necesita quien lo ejecuta: sysadmin (CREATE LOGIN, CREATE USER
   y GRANT en MILMAIN).

   Qué otorga (mínimo privilegio, nada de escritura, nada de MILLET_INTEGRACION):
     - millet_erp_lector_aw: SELECT sobre las tablas SYSADM que leen
       AwClientesSqlOrigen.cs, AwClientesPedidosOrigen.cs y AwProductosSqlOrigen.cs:
         clientes:  KU_KUNDEN, KA_ZAHLBED, KA_BONITAET
         historial: BW_AUFTR_KOPF (pedidos), BW_ANGEB_KOPF (cotizaciones). Solo se cuentan
                    por cliente (AH_IDENT) para unificar clientes con RFC repetido. Se agregaron
                    el 29-oct-2026: la pantalla de unificar fallaba con «No se pudieron cargar
                    los clientes de este RFC».
         productos: BA_PRODUKTE, BA_PRODUKTE_BEZ, BA_STUKL, BA_STUKL_BEARB,
                    BA_LAGMA, KA_WGR
         notas de crédito (5-nov): BW_GUTSCH_KOPF/_HIST/_POS, KA_FIRMA_MITARB; también
                    BW_AUFTR_STKL y LG_LAGERORTE (5-nov)
         OTR (base OTR): OTR_ROUTES, OTR_CONTAINERS_ACTIONS, OTR_CONTAINER_POSITIONS
         (KA_WAEHRUNGEN lo lee millet_erp_integracion, ya en 04_create_login.sql)
     - Si cambian esos orígenes, actualizar este script y la lista de arriba.

   ⚠️ La contraseña NO va en este script ni en el repo: se pasa por SQLCMD var
   (sqlcmd -v Password=...) o se edita en el momento. Después de crearla →
   secreto de Key Vault con la cadena de conexión del lector.
   ========================================================================== */

-- :setvar Password "REEMPLAZAR_EN_EJECUCION"
IF SUSER_ID('millet_erp_lector_aw') IS NULL
BEGIN
    CREATE LOGIN millet_erp_lector_aw
        WITH PASSWORD = N'$(Password)',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = MILMAIN;
    PRINT 'Login millet_erp_lector_aw creado.';
END
ELSE
    PRINT 'Login millet_erp_lector_aw ya existe — sin cambios.';
GO

USE MILMAIN;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_lector_aw') IS NULL
    CREATE USER millet_erp_lector_aw FOR LOGIN millet_erp_lector_aw;
GO
/* Clientes */
GRANT SELECT ON SYSADM.KU_KUNDEN       TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.KA_ZAHLBED      TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.KA_BONITAET     TO millet_erp_lector_aw;
/* Historial de pedidos y cotizaciones por cliente (unificación de RFC repetido) */
GRANT SELECT ON SYSADM.BW_AUFTR_KOPF   TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BW_ANGEB_KOPF   TO millet_erp_lector_aw;
/* Productos */
GRANT SELECT ON SYSADM.BA_PRODUKTE     TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_PRODUKTE_BEZ TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_STUKL        TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_STUKL_BEARB  TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_LAGMA        TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.KA_WGR          TO millet_erp_lector_aw;
/* Notas de crédito de A+W (AwNotasCreditoLectorAdapter, conexión AwClientesDb). Otorgado por TI el 5-nov-2026
   por correo; se deja escrito. BW_GUTSCH_POS y los dos de abajo aún no los lee ningún adaptador del repo. */
GRANT SELECT ON SYSADM.BW_GUTSCH_KOPF  TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BW_GUTSCH_HIST  TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BW_GUTSCH_POS   TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.KA_FIRMA_MITARB TO millet_erp_lector_aw;
/* Componentes del pedido y ubicaciones de almacén: otorgado por TI el 5-nov-2026 por correo; se deja escrito. */
GRANT SELECT ON SYSADM.BW_AUFTR_STKL   TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.LG_LAGERORTE    TO millet_erp_lector_aw;
GO

/* OTR (Exportacion/OtrTablasAdapter, conexión AwClientesDb): rutas y contenedores de la base OTR.
   Otorgado por TI el 5-nov-2026 por correo; se deja escrito. El adaptador no llama a
   CONSULTA_PEDIDOS_ESCAN_OTR_ALCIM, por eso no se da EXECUTE. */
USE OTR;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_lector_aw') IS NULL
    CREATE USER millet_erp_lector_aw FOR LOGIN millet_erp_lector_aw;
GO
GRANT SELECT ON SYSADM.OTR_ROUTES               TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.OTR_CONTAINERS_ACTIONS   TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.OTR_CONTAINER_POSITIONS  TO millet_erp_lector_aw;
GO

/* Rollback:
   USE OTR;
   DROP USER millet_erp_lector_aw;
   USE MILMAIN;
   DROP USER millet_erp_lector_aw;
   DROP LOGIN millet_erp_lector_aw;
*/
