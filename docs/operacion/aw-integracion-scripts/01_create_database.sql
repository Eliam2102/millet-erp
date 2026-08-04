/* =============================================================================
   01_create_database.sql — BD de integración A+W ⇄ ERP (ADR-0048)
   Instancia: SERDATA\AWBUSINESS (SER-DATA); la BD de A+W es MILMAIN.
   Ejecutar como: sysadmin, en SSMS o sqlcmd. Idempotente.

   Crea MILLET_INTEGRACION (producción) y MILLET_INTEGRACION_DEV (dev/qa),
   con la MISMA colación que MILMAIN para evitar collation conflicts en
   las vistas cross-database (JOIN/WHERE sobre columnas nvarchar).
   ========================================================================== */

/* Pre-chequeos con SET NOEXEC ON: un RAISERROR 16 + RETURN solo aborta el
   batch actual — los batches después de GO correrían igual. NOEXEC persiste
   entre batches y suspende la ejecución del resto del script. */
SET NOEXEC OFF;

DECLARE @login sysname = SUSER_SNAME();
IF IS_SRVROLEMEMBER('sysadmin') = 0 AND IS_SRVROLEMEMBER('dbcreator') = 0
BEGIN
    RAISERROR('El login "%s" no es sysadmin ni dbcreator — conéctate como sysadmin y reintenta.', 16, 1, @login);
    SET NOEXEC ON;
END;
GO

DECLARE @colacion sysname = CONVERT(sysname, DATABASEPROPERTYEX('MILMAIN', 'Collation'));
IF @colacion IS NULL
BEGIN
    RAISERROR('La BD MILMAIN no existe en esta instancia — abortando.', 16, 1);
    SET NOEXEC ON;
    RETURN;
END;

PRINT 'Colación de MILMAIN: ' + @colacion;

/* Sanidad: la colación viene de DATABASEPROPERTYEX, pero como se concatena
   en SQL dinámico validamos que exista en el catálogo (COLLATE no acepta el
   nombre entre corchetes, así que QUOTENAME no es opción). */
IF NOT EXISTS (SELECT 1 FROM sys.fn_helpcollations() WHERE name = @colacion)
BEGIN
    RAISERROR('Colación "%s" no reconocida por sys.fn_helpcollations — abortando.', 16, 1, @colacion);
    SET NOEXEC ON;
    RETURN;
END;

IF DB_ID('MILLET_INTEGRACION') IS NULL
BEGIN
    DECLARE @sql1 nvarchar(max) =
        N'CREATE DATABASE MILLET_INTEGRACION COLLATE ' + @colacion;
    EXEC (@sql1);
END;

IF DB_ID('MILLET_INTEGRACION_DEV') IS NULL
BEGIN
    DECLARE @sql2 nvarchar(max) =
        N'CREATE DATABASE MILLET_INTEGRACION_DEV COLLATE ' + @colacion;
    EXEC (@sql2);
END;

/* Verificación honesta: DB_ID re-consultado DESPUÉS del EXEC (un error dentro
   del SQL dinámico no aborta este batch — no confiar en "no tronó"). */
IF DB_ID('MILLET_INTEGRACION') IS NOT NULL
    PRINT 'MILLET_INTEGRACION OK (colación ' + CONVERT(sysname, DATABASEPROPERTYEX('MILLET_INTEGRACION', 'Collation')) + ').';
ELSE
BEGIN
    RAISERROR('MILLET_INTEGRACION NO se creó — revisa los errores de arriba.', 16, 1);
    SET NOEXEC ON;
END;

IF DB_ID('MILLET_INTEGRACION_DEV') IS NOT NULL
    PRINT 'MILLET_INTEGRACION_DEV OK (colación ' + CONVERT(sysname, DATABASEPROPERTYEX('MILLET_INTEGRACION_DEV', 'Collation')) + ').';
ELSE
BEGIN
    RAISERROR('MILLET_INTEGRACION_DEV NO se creó — revisa los errores de arriba.', 16, 1);
    SET NOEXEC ON;
END;
GO

/* Recovery SIMPLE: la tabla-puente es cola/bitácora de integración, no
   sistema de registro (la fuente de verdad del ERP es ingesta_control en
   Postgres). Evita crecimiento de log en SER-DATA. */
IF DB_ID('MILLET_INTEGRACION') IS NOT NULL
    ALTER DATABASE MILLET_INTEGRACION SET RECOVERY SIMPLE;
IF DB_ID('MILLET_INTEGRACION_DEV') IS NOT NULL
    ALTER DATABASE MILLET_INTEGRACION_DEV SET RECOVERY SIMPLE;
GO

/* Restaurar la sesión (NOEXEC persiste a nivel conexión). */
SET NOEXEC OFF;
GO

/* Rollback (SOLO si hay que deshacer todo; destruye datos):
   -- DROP DATABASE MILLET_INTEGRACION;
   -- DROP DATABASE MILLET_INTEGRACION_DEV;
*/
