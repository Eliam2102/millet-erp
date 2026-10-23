/* =============================================================================
   09_create_login_lector.sql — Login de SOLO LECTURA para los lectores de
   clientes y productos de A+W (módulo Millet.Integraciones.Aw)
   Ejecutar como: sysadmin, en SER-DATA\AWBUSINESS. Idempotente.

   Permisos que necesita quien lo ejecuta: sysadmin (CREATE LOGIN, CREATE USER
   y GRANT en MILMAIN).

   Qué otorga (mínimo privilegio, nada de escritura, nada de MILLET_INTEGRACION):
     - millet_erp_lector_aw: SELECT sobre las tablas SYSADM que leen
       AwClientesSqlOrigen.cs y AwProductosSqlOrigen.cs:
         clientes:  KU_KUNDEN, KA_ZAHLBED, KA_BONITAET
         productos: BA_PRODUKTE, BA_PRODUKTE_BEZ, BA_STUKL, BA_STUKL_BEARB,
                    BA_LAGMA, KA_WGR
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
/* Productos */
GRANT SELECT ON SYSADM.BA_PRODUKTE     TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_PRODUKTE_BEZ TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_STUKL        TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_STUKL_BEARB  TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.BA_LAGMA        TO millet_erp_lector_aw;
GRANT SELECT ON SYSADM.KA_WGR          TO millet_erp_lector_aw;
GO

/* Rollback:
   USE MILMAIN;
   DROP USER millet_erp_lector_aw;
   DROP LOGIN millet_erp_lector_aw;
*/
