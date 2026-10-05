-- CDC en el sandbox (prueba del lector de cambios; requiere MSSQL_AGENT_ENABLED). Idempotente. Se aplica DESPUÉS del seed.
IF (SELECT is_cdc_enabled FROM sys.databases WHERE name = DB_NAME()) = 0 EXEC sys.sp_cdc_enable_db;
GO
DECLARE @t TABLE(n sysname);
INSERT @t VALUES('KU_KUNDEN'),('BA_PRODUKTE'),('BA_STUKL'),('BA_PRODUKTE_BEZ');
DECLARE @n sysname;
DECLARE c CURSOR FOR SELECT n FROM @t;
OPEN c; FETCH c INTO @n;
WHILE @@FETCH_STATUS = 0 BEGIN
  IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE source_object_id = OBJECT_ID('SYSADM.' + @n))
    EXEC sys.sp_cdc_enable_table @source_schema='SYSADM', @source_name=@n, @role_name=NULL, @supports_net_changes=0;
  FETCH c INTO @n;
END
CLOSE c; DEALLOCATE c;
GO
-- El lector de solo lectura necesita ver el esquema cdc y ejecutar sus funciones de LSN.
GRANT SELECT ON SCHEMA::cdc TO aw_ro;
GRANT EXECUTE ON SCHEMA::cdc TO aw_ro;
GRANT VIEW DEFINITION ON SCHEMA::cdc TO aw_ro;
