-- Prueba de CDC sobre la copia local AW_FULL (nunca el A+W real). Requiere SQL Server Agent.
USE AW_FULL;
IF (SELECT is_cdc_enabled FROM sys.databases WHERE name='AW_FULL')=0 EXEC sys.sp_cdc_enable_db;
DECLARE @t TABLE(n sysname);
INSERT @t VALUES('KU_KUNDEN'),('BA_PRODUKTE'),('BA_STUKL'),('BA_PRODUKTE_BEZ');
DECLARE @n sysname;
DECLARE c CURSOR FOR SELECT n FROM @t;
OPEN c; FETCH c INTO @n;
WHILE @@FETCH_STATUS=0 BEGIN
  IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE source_object_id=OBJECT_ID('SYSADM.'+@n))
    EXEC sys.sp_cdc_enable_table @source_schema='SYSADM', @source_name=@n, @role_name=NULL, @supports_net_changes=1;
  FETCH c INTO @n;
END
CLOSE c; DEALLOCATE c;
SELECT capture_instance, OBJECT_NAME(source_object_id) tabla FROM cdc.change_tables;
SELECT job_type FROM msdb.dbo.cdc_jobs;
