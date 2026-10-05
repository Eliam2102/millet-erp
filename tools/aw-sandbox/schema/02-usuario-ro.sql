-- Login de solo lectura. Contraseña por variable sqlcmd: -v RO_PASSWORD=... (no se versiona).
USE master;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'aw_ro')
    EXEC(N'CREATE LOGIN aw_ro WITH PASSWORD = N''$(RO_PASSWORD)'', CHECK_POLICY = OFF');
ELSE
    EXEC(N'ALTER LOGIN aw_ro WITH PASSWORD = N''$(RO_PASSWORD)''');
GO
USE AW_SANDBOX;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'aw_ro') CREATE USER aw_ro FOR LOGIN aw_ro;
GRANT SELECT ON SCHEMA::SYSADM TO aw_ro;
GO
