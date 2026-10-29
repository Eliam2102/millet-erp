-- PROPUESTA · no se ejecuta sin visto bueno de TI (Millet) y de Jorge
-- Portal de Pinturas (ASP.NET WebForms, BD Pinturas_NEW) <-> ERP, vía MILLET_INTEGRACION(_DEV).
-- Ejecutar en SER-DATA\AWBUSINESS como sysadmin, con: sqlcmd -b -v Password="..." -i 16_create_pinturas.sql
-- (-b para que el guard de BD detenga el resto de lotes). No ejecutar en una base interna del proveedor A+W.
--
-- Qué crea (dbo, DDL puente propiedad del ERP):
--   Catálogos que PUBLICA el ERP (cada ~10 s + pasada completa nocturna) y Pinturas LEE:
--     pinturas_cliente, pinturas_servicio, pinturas_obra, pinturas_empleado
--     · Sin borrado físico: la baja es activo = 0; updated_at marca el último cambio publicado.
--     · pinturas_servicio NO lleva precio: el precio por m2 se queda en el catálogo local de Pinturas.
--     · Obra viaja como texto (clave + nombre), sin validación contra proyectos por ahora.
--     · Cliente: solo lo que el ERP tiene hoy (sin contacto ni celular).
--   Pedido que INSERTA Pinturas y el ERP LEE y confirma:
--     pinturas_pedido (una fila por orden transmitida, línea única; folio_orden único = idempotencia)
--     · Reenvío tras error: el portal corrige la orden y la reenvía con el MISMO folio = UPDATE de la fila con los datos
--       corregidos y erp_resultado/erp_error en NULL (vuelve a la cola). El ERP ignora un folio que ya generó un
--       PedidoFacturable (erp_resultado = 1), así que una orden creada no se puede duplicar.
--     · Acuse del ERP: erp_resultado (1 creado, 2 error), erp_pedido_id (PedidoFacturable.Id),
--       erp_error, erp_procesado_at. Pendientes = erp_resultado IS NULL.
-- Logins: millet_erp_integracion (ya existe, 04_create_login.sql) y millet_pinturas_portal (nuevo).
-- La contraseña NO va en este script ni en el repo: se pasa por SQLCMD var (-v Password=...).
-- Después de crearla -> secreto de Key Vault / configuración del portal.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() NOT IN (N'MILLET_INTEGRACION',N'MILLET_INTEGRACION_DEV')
    THROW 50001, 'Seleccione la BD puente MILLET_INTEGRACION(_DEV).', 1;
BEGIN TRANSACTION;

-- Catálogos publicados por el ERP.
IF OBJECT_ID(N'dbo.pinturas_cliente', N'U') IS NULL
CREATE TABLE dbo.pinturas_cliente (
    clave nvarchar(50) NOT NULL PRIMARY KEY,
    razon_social nvarchar(200) NOT NULL,
    nombre_comercial nvarchar(200) NULL,
    telefono nvarchar(50) NULL,
    email nvarchar(200) NULL,
    activo bit NOT NULL DEFAULT 1,
    updated_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID(N'dbo.pinturas_servicio', N'U') IS NULL
CREATE TABLE dbo.pinturas_servicio (
    clave nvarchar(50) NOT NULL PRIMARY KEY,
    nombre nvarchar(200) NOT NULL, -- color y acabado
    activo bit NOT NULL DEFAULT 1,
    updated_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID(N'dbo.pinturas_obra', N'U') IS NULL
CREATE TABLE dbo.pinturas_obra (
    clave nvarchar(50) NOT NULL PRIMARY KEY,
    nombre nvarchar(200) NOT NULL,
    activo bit NOT NULL DEFAULT 1,
    updated_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID(N'dbo.pinturas_empleado', N'U') IS NULL
CREATE TABLE dbo.pinturas_empleado (
    clave nvarchar(50) NOT NULL PRIMARY KEY,
    nombre nvarchar(200) NOT NULL,
    activo bit NOT NULL DEFAULT 1,
    updated_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Pedido de Pinturas: una fila por orden transmitida, siempre con una sola línea (servicio, m2 totales, precio promedio).
IF OBJECT_ID(N'dbo.pinturas_pedido', N'U') IS NULL
CREATE TABLE dbo.pinturas_pedido (
    id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    folio_orden nvarchar(50) NOT NULL UNIQUE, -- folio de la orden de Pinturas: clave de idempotencia
    cliente_clave nvarchar(50) NOT NULL,
    cliente_nombre nvarchar(200) NOT NULL,
    obra_clave nvarchar(50) NULL,
    obra_nombre nvarchar(200) NULL,
    comentarios nvarchar(1000) NULL,
    servicio_clave nvarchar(50) NOT NULL,
    m2 decimal(18,6) NOT NULL CHECK (m2 > 0),
    precio_m2 decimal(18,6) NOT NULL CHECK (precio_m2 >= 0), -- precio promedio = importe total / m2 totales
    iva_porcentaje decimal(5,2) NOT NULL CHECK (iva_porcentaje >= 0), -- IVA de la orden; el ERP lo traduce a indicador de impuesto
    moneda nchar(3) NOT NULL DEFAULT N'MXN',
    creado_at datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    erp_resultado tinyint NULL CHECK (erp_resultado IN (1,2)), -- 1 creado, 2 error
    erp_pedido_id uniqueidentifier NULL, -- PedidoFacturable.Id
    erp_error nvarchar(500) NULL,
    erp_procesado_at datetime2(3) NULL
);
-- Longitudes (folio_orden y claves nvarchar(50)): a confirmar con TI contra Pinturas_NEW.
-- Cola de pendientes del ERP.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.pinturas_pedido') AND name = N'ix_pinturas_pedido_pendiente')
CREATE INDEX ix_pinturas_pedido_pendiente ON dbo.pinturas_pedido (creado_at, id) WHERE erp_resultado IS NULL;

COMMIT;
GO

-- Login del portal (el de millet_erp_integracion ya lo crea 04_create_login.sql).
-- :setvar Password "REEMPLAZAR_EN_EJECUCION"
IF SUSER_ID('millet_pinturas_portal') IS NULL
BEGIN
    CREATE LOGIN millet_pinturas_portal
        WITH PASSWORD = N'$(Password)',
             CHECK_POLICY = ON,
             DEFAULT_DATABASE = MILLET_INTEGRACION;
    PRINT 'Login millet_pinturas_portal creado.';
END
ELSE
    PRINT 'Login millet_pinturas_portal ya existe — sin cambios.';
GO

IF DB_NAME() NOT IN (N'MILLET_INTEGRACION',N'MILLET_INTEGRACION_DEV')
    THROW 50001, 'Seleccione la BD puente MILLET_INTEGRACION(_DEV).', 1;
BEGIN TRANSACTION;
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NULL
    CREATE USER millet_erp_integracion FOR LOGIN millet_erp_integracion;
IF DATABASE_PRINCIPAL_ID('millet_pinturas_portal') IS NULL
    CREATE USER millet_pinturas_portal FOR LOGIN millet_pinturas_portal;

-- ERP: publica (altas, cambios y bajas lógicas; sin DELETE) los 4 catálogos.
GRANT SELECT, INSERT, UPDATE ON dbo.pinturas_cliente  TO millet_erp_integracion;
GRANT SELECT, INSERT, UPDATE ON dbo.pinturas_servicio TO millet_erp_integracion;
GRANT SELECT, INSERT, UPDATE ON dbo.pinturas_obra     TO millet_erp_integracion;
GRANT SELECT, INSERT, UPDATE ON dbo.pinturas_empleado TO millet_erp_integracion;
-- ERP: lee pedidos y solo escribe el acuse.
GRANT SELECT ON dbo.pinturas_pedido TO millet_erp_integracion;
GRANT UPDATE (erp_resultado, erp_pedido_id, erp_error, erp_procesado_at) ON dbo.pinturas_pedido TO millet_erp_integracion;

-- Portal: solo lee los catálogos; inserta pedidos, lee el acuse y, para reenviar un pedido con error, corrige
-- los datos de la orden y deja en NULL erp_resultado y erp_error. erp_pedido_id y erp_procesado_at son solo del ERP.
GRANT SELECT ON dbo.pinturas_cliente  TO millet_pinturas_portal;
GRANT SELECT ON dbo.pinturas_servicio TO millet_pinturas_portal;
GRANT SELECT ON dbo.pinturas_obra     TO millet_pinturas_portal;
GRANT SELECT ON dbo.pinturas_empleado TO millet_pinturas_portal;
GRANT SELECT, INSERT ON dbo.pinturas_pedido TO millet_pinturas_portal;
GRANT UPDATE (cliente_clave, cliente_nombre, obra_clave, obra_nombre, comentarios, servicio_clave, m2, precio_m2, iva_porcentaje,
             erp_resultado, erp_error) ON dbo.pinturas_pedido TO millet_pinturas_portal;
DENY DELETE ON dbo.pinturas_pedido TO millet_pinturas_portal;
COMMIT;
GO

-- Verificación (esperado: 5 tablas; SELECT, INSERT, UPDATE del ERP en catálogos y SELECT + UPDATE de 4 columnas en pedido;
-- portal: SELECT en 5, INSERT y UPDATE de 11 columnas en pedido, DENY DELETE en pedido; índice filtrado con filtro erp_resultado IS NULL).
SELECT name FROM sys.tables WHERE name LIKE N'pinturas[_]%' ORDER BY name;
SELECT pr.name AS principal, OBJECT_NAME(p.major_id) AS objeto, p.permission_name, p.state_desc,
       COL_NAME(p.major_id, p.minor_id) AS columna
FROM sys.database_permissions p
JOIN sys.database_principals pr ON pr.principal_id = p.grantee_principal_id
WHERE pr.name IN (N'millet_erp_integracion', N'millet_pinturas_portal') AND OBJECT_NAME(p.major_id) LIKE N'pinturas[_]%'
ORDER BY pr.name, objeto, p.permission_name, columna;
SELECT name, filter_definition FROM sys.indexes WHERE name = N'ix_pinturas_pedido_pendiente';
GO

/* Rollback (en cada BD donde se aplicó; borra los datos de Pinturas en la base puente):
   BEGIN TRANSACTION;
   DROP TABLE IF EXISTS dbo.pinturas_pedido;
   DROP TABLE IF EXISTS dbo.pinturas_empleado;
   DROP TABLE IF EXISTS dbo.pinturas_obra;
   DROP TABLE IF EXISTS dbo.pinturas_servicio;
   DROP TABLE IF EXISTS dbo.pinturas_cliente;
   DROP USER IF EXISTS millet_pinturas_portal;
   COMMIT;
   -- Solo si no queda en ninguna BD (DEV incluida):
   -- DROP LOGIN millet_pinturas_portal;
   -- No se elimina millet_erp_integracion: lo comparten los demás flujos (04_create_login.sql);
   -- sus permisos sobre pinturas_* se van con las tablas.
*/
