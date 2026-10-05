SET NOCOUNT ON;
DECLARE @s nvarchar(max) = N'';
SELECT @s += N'ALTER TABLE SYSADM.[' + t.name + N'] DROP CONSTRAINT [' + d.name + N'];' + CHAR(10)
FROM sys.default_constraints d JOIN sys.tables t ON t.object_id = d.parent_object_id WHERE d.name LIKE N'DF_tmp[_]%';
EXEC sp_executesql @s;
GO
