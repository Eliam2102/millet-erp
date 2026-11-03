-- Propuesta para el contrato I13.4 · no aplicar sin acuerdo
-- Ejecutar únicamente después de acordar con Millet y Jorge, en MILLET_INTEGRACION(_DEV).
-- No ejecutar en una base interna del proveedor A+W. DDL puente propiedad del ERP.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() NOT IN (N'MILLET_INTEGRACION',N'MILLET_INTEGRACION_DEV')
    THROW 50001, 'Seleccione la BD puente MILLET_INTEGRACION(_DEV).', 1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.aw_otr_envio', N'U') IS NULL
CREATE TABLE dbo.aw_otr_envio (
    otr_envio_id nvarchar(100) NOT NULL,
    version bigint NOT NULL CHECK (version > 0),
    cliente_numero_aw nvarchar(50) NOT NULL,
    sucursal_clave_aw nvarchar(50) NOT NULL,
    cliente nvarchar(200) NOT NULL,
    direccion_entrega nvarchar(1000) NOT NULL,
    producto nvarchar(500),
    numero_contenedor nvarchar(200),
    ruta nvarchar(200),
    bultos int CHECK (bultos >= 0),
    peso_kg numeric(18,6) CHECK (peso_kg >= 0),
    fecha_carga_prevista date,
    booking nvarchar(200),
    bl_guia nvarchar(200),
    operacion nvarchar(20) NOT NULL CHECK (operacion IN ('Alta','Modificacion','Cancelacion')),
    creada_at datetimeoffset(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    erp_resultado nvarchar(30),
    erp_motivo nvarchar(500),
    erp_procesada_at datetimeoffset(3),
    PRIMARY KEY (otr_envio_id, version)
);
IF OBJECT_ID(N'dbo.aw_otr_envio_linea', N'U') IS NULL
CREATE TABLE dbo.aw_otr_envio_linea (
    otr_envio_id nvarchar(100) NOT NULL,
    version bigint NOT NULL,
    numero_pedido nvarchar(50) NOT NULL,
    posicion int NOT NULL CHECK (posicion > 0),
    cantidad numeric(18,6) NOT NULL CHECK (cantidad > 0),
    unidad nvarchar(20) NOT NULL,
    PRIMARY KEY (otr_envio_id, version, numero_pedido, posicion),
    FOREIGN KEY (otr_envio_id,version) REFERENCES dbo.aw_otr_envio(otr_envio_id,version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.aw_otr_envio') AND name = N'ix_aw_otr_pendiente')
CREATE INDEX ix_aw_otr_pendiente ON dbo.aw_otr_envio (erp_resultado,otr_envio_id,version);
IF OBJECT_ID(N'dbo.erp_otr_estado', N'U') IS NULL
CREATE TABLE dbo.erp_otr_estado (
    salida_id uniqueidentifier PRIMARY KEY,
    otr_envio_id nvarchar(100) NOT NULL,
    tipo nvarchar(50) NOT NULL,
    version_erp int NOT NULL CHECK (version_erp > 0),
    intento int NOT NULL DEFAULT 1 CHECK (intento > 0),
    valor_json nvarchar(max) NOT NULL CHECK (ISJSON(valor_json) = 1),
    creada_at datetimeoffset(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    aw_acuse_at datetimeoffset(3),
    aw_resultado nvarchar(30),
    aw_motivo nvarchar(500),
    UNIQUE (otr_envio_id,tipo,version_erp,intento)
);

COMMIT;
GO
