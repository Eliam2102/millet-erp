/* =============================================================================
   15_create_login_lector_produccion.sql — Login de SOLO LECTURA sobre alcimdb
   (A+W Production) para la entrada de piezas a producto terminado (PT5.1)
   Ejecutar como: sysadmin, en 192.168.1.8\AWPRODUCTION. Idempotente.
   PROPUESTA: no se ejecuta sin visto bueno de TI.

   Origen (TI, 27-oct, punto 19 y E08-1): la pieza terminada queda en
   alcimdb.awbar_bearbeit con fertig = 1 y esnr_ist en las estaciones de
   producto terminado. El ERP solo consulta; no escribe en alcimdb ni pide
   disparadores ni personalizaciones (ese sistema no los admite).

   Qué otorga (mínimo privilegio): SELECT sobre las cinco tablas de la consulta
   de referencia de TI (anexo del documento del 27-oct), nada más:
     awbar_bearbeit, pool_bearbeit, pool_teile, pool_pos, pool_glasart

   Alcance de red (lo resuelve Eliam en Azure con TI): la conexión híbrida actual
   (hc-aw-business-sql) llega a AWBUSINESS y no ve esta instancia. Hace falta una
   tercera conexión (hc-aw-production-sql) hacia el puerto de AWPRODUCTION; por ser
   instancia con nombre, ese puerto debe quedar estático.

   ⚠️ La contraseña NO va en este script ni en el repo: se pasa por SQLCMD var
   (sqlcmd -v Password=...) o se edita en el momento. Después → secreto de Key
   Vault con la cadena de conexión del lector.
   ========================================================================== */

-- :setvar Password "REEMPLAZAR_EN_EJECUCION"
IF SUSER_ID('millet_erp_lector_produccion') IS NULL
BEGIN
    CREATE LOGIN millet_erp_lector_produccion
        WITH PASSWORD = N'$(Password)',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = alcimdb;
    PRINT 'Login millet_erp_lector_produccion creado.';
END
ELSE
    PRINT 'Login millet_erp_lector_produccion ya existe — sin cambios.';
GO

USE alcimdb;
GO
IF DATABASE_PRINCIPAL_ID('millet_erp_lector_produccion') IS NULL
    CREATE USER millet_erp_lector_produccion FOR LOGIN millet_erp_lector_produccion;
GO
GRANT SELECT ON dbo.awbar_bearbeit TO millet_erp_lector_produccion;
GRANT SELECT ON dbo.pool_bearbeit  TO millet_erp_lector_produccion;
GRANT SELECT ON dbo.pool_teile     TO millet_erp_lector_produccion;
GRANT SELECT ON dbo.pool_pos       TO millet_erp_lector_produccion;
GRANT SELECT ON dbo.pool_glasart   TO millet_erp_lector_produccion;
GO

/* Comprobación (como el lector): debe regresar piezas del día y fallar con
   cualquier tabla fuera de la lista.
   SELECT TOP (10) ab.etikettnr, ab.auftnr, ab.pos, ab.teile_nr, ab.bearb_zeit, ab.esnr_ist
     FROM dbo.awbar_bearbeit AS ab WITH (NOLOCK)
    WHERE ab.fertig = 1 AND ab.bearb_zeit >= CAST(GETDATE() AS date)
    ORDER BY ab.bearb_zeit DESC;
*/

/* Rollback:
   USE alcimdb;
   DROP USER millet_erp_lector_produccion;
   DROP LOGIN millet_erp_lector_produccion;
*/
