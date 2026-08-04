/* =============================================================================
   02_create_table.sql — Tabla-puente aw_solicitud_pedido (ADR-0048, D18)
   Ejecutar en: MILLET_INTEGRACION y MILLET_INTEGRACION_DEV (cambiar USE).
   Idempotente.

   Contrato: docs/integration/04-ingesta-pedidos-facturacion.md §2.
   - A+W escribe:  numero_pedido, operacion, version (solicitud_id y
                   creada_at los ponen los defaults).
   - El ERP escribe: erp_pedido_id (claim, NUNCA se borra), estado_facturacion,
                   uuid, resultado, motivo, procesada_at.
   - Pendiente = resultado IS NULL OR resultado = 3 (Pospuesta se relee).
   ========================================================================== */

USE MILLET_INTEGRACION;  -- repetir con MILLET_INTEGRACION_DEV
GO

IF OBJECT_ID('dbo.aw_solicitud_pedido', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.aw_solicitud_pedido
    (
        solicitud_id       uniqueidentifier  NOT NULL
            CONSTRAINT DF_asp_solicitud_id DEFAULT NEWID(),
        numero_pedido      nvarchar(50)      NOT NULL,
        operacion          tinyint           NOT NULL,  -- 1 Alta | 2 Modificacion | 3 Cancelacion
        [version]          bigint            NOT NULL,  -- estrictamente creciente por pedido
        creada_at          datetimeoffset(3) NOT NULL
            CONSTRAINT DF_asp_creada_at DEFAULT SYSDATETIMEOFFSET(),

        -- ===== write-back del ERP (A+W NO escribe estas columnas) =====
        erp_pedido_id      uniqueidentifier  NULL,      -- claim; persiste siempre
        estado_facturacion nvarchar(20)      NULL,      -- SinFacturar | Facturado | Cancelado
        [uuid]             nvarchar(36)      NULL,      -- folio fiscal CFDI
        resultado          tinyint           NULL,      -- NULL pendiente | 1 Aplicada | 2 Rechazada | 3 Pospuesta | 4 Error
        motivo             nvarchar(500)     NULL,
        procesada_at       datetimeoffset(3) NULL,

        CONSTRAINT PK_aw_solicitud_pedido PRIMARY KEY CLUSTERED (solicitud_id),
        CONSTRAINT UQ_asp_pedido_version  UNIQUE (numero_pedido, [version]),
        CONSTRAINT CK_asp_operacion       CHECK (operacion IN (1, 2, 3)),
        CONSTRAINT CK_asp_resultado       CHECK (resultado IS NULL OR resultado IN (1, 2, 3, 4))
    );
    PRINT 'dbo.aw_solicitud_pedido creada.';
END
ELSE
    PRINT 'dbo.aw_solicitud_pedido ya existe — sin cambios.';
GO

/* Índice de barrido del worker:
   SELECT TOP(@max) ... WHERE resultado IS NULL OR resultado = 3
   ORDER BY numero_pedido, version */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_asp_barrido'
                 AND object_id = OBJECT_ID('dbo.aw_solicitud_pedido'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_asp_barrido
        ON dbo.aw_solicitud_pedido (resultado, numero_pedido, [version])
        INCLUDE (operacion, creada_at);
    PRINT 'IX_asp_barrido creado.';
END
GO

/* Índice para el write-back por pedido (UPDATE ... WHERE numero_pedido = @np)
   — cubierto por UQ_asp_pedido_version (numero_pedido primero). No se
   requiere índice adicional. */

/* Rollback:
   -- DROP TABLE dbo.aw_solicitud_pedido;
*/
