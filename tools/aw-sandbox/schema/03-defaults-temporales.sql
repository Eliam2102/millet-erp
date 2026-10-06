-- Solo durante el seed: el esquema es el DDL íntegro del A+W real (columnas NOT NULL sin default), pero el seed sintético
-- solo informa las columnas que importan. Se crean defaults temporales (DF_tmp_*) y 04-quitar-defaults.sql los elimina.
SET NOCOUNT ON;
DECLARE @s nvarchar(max) = N'';
SELECT @s += N'ALTER TABLE SYSADM.[' + t.name + N'] ADD CONSTRAINT [DF_tmp_' + t.name + N'_' + c.name + N'] DEFAULT '
  + CASE WHEN ty.name IN ('char','varchar','nchar','nvarchar','text','ntext') THEN N''''''
         WHEN ty.name IN ('date','datetime','datetime2','smalldatetime') THEN N'''1900-01-01'''
         WHEN ty.name = 'uniqueidentifier' THEN N'NEWID()'
         WHEN ty.name IN ('binary','varbinary') THEN N'0x'
         ELSE N'0' END + N' FOR [' + c.name + N'];' + CHAR(10)
FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.schema_id = SCHEMA_ID('SYSADM') AND c.is_nullable = 0 AND c.is_identity = 0 AND c.is_computed = 0 AND ty.name NOT IN ('timestamp','rowversion')
  AND NOT EXISTS (SELECT 1 FROM sys.default_constraints d WHERE d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id);
EXEC sp_executesql @s;
GO
