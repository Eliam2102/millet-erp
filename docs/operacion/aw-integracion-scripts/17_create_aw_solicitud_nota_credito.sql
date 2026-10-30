/* =============================================================================
   17_create_aw_solicitud_nota_credito.sql — Tabla puente de notas de crédito de
   clientes (86e3wjt5r). Ejecutar en: MILLET_INTEGRACION_DEV primero;
   MILLET_INTEGRACION al arranque (cambiar USE). Idempotente.
   PROPUESTA del ERP: no se ejecuta sin visto bueno de TI.

   Regla de Millet (27-oct): no se crea ni modifica nada dentro de las bases de
   A+W. Esta tabla vive en MILLET_INTEGRACION(_DEV), igual que aw_solicitud_pedido
   (script 02) y aw_retorno (script 14). Es una tabla aparte de la de pedidos:
   su llave (nota + versión) y sus datos son otros.

   Contrato (mismo molde que 02):
   - A+W escribe (un job, igual que con los pedidos; POR CONFIRMAR con TI quién
     lo llena y desde dónde vive la nota en A+W):
       numero_nota, version, numero_pedido, factura_folio (opcional), cliente_ref,
       tipo_nota, importe, descripcion (solicitud_id y creada_at, los defaults).
   - El ERP escribe: erp_nota_credito_id (claim, nunca se borra), estado_erp,
       resultado, motivo, procesada_at.
   - Pendiente = resultado IS NULL. Aplicada (1) y Rechazada (2) son finales: una
     nota rechazada (factura inexistente, cliente distinto, importe mayor que el
     saldo) se corrige con una versión nueva (misma nota, version + 1).
   - numero_pedido: pedido A+W de la factura origen. factura_folio solo hace falta
     si el pedido tiene más de una factura timbrada.
   - importe: total de la nota con IVA, en la moneda de la factura.
   - La nota llega al ERP «Por autorizar»; al timbrarse, el ERP devuelve la fila
     tipo Documento / NotaCredito en aw_retorno (script 14), con el folio fiscal.
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;  -- producción: MILLET_INTEGRACION
GO

IF OBJECT_ID('dbo.aw_solicitud_nota_credito', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.aw_solicitud_nota_credito
    (
        solicitud_id        uniqueidentifier  NOT NULL
            CONSTRAINT DF_asnc_solicitud_id DEFAULT NEWID(),
        numero_nota         nvarchar(50)      NOT NULL,
        [version]           bigint            NOT NULL,  -- estrictamente creciente por nota
        numero_pedido       nvarchar(50)      NOT NULL,
        factura_folio       nvarchar(50)      NULL,
        cliente_ref         nvarchar(50)      NOT NULL,  -- mismo número de cliente que vw_erp_cliente
        tipo_nota           nvarchar(20)      NOT NULL,  -- Bonificacion | Devolucion
        importe             decimal(18, 2)    NOT NULL,
        descripcion         nvarchar(1000)    NULL,
        creada_at           datetimeoffset(3) NOT NULL
            CONSTRAINT DF_asnc_creada_at DEFAULT SYSDATETIMEOFFSET(),

        -- ===== acuse del ERP (A+W NO escribe estas columnas) =====
        erp_nota_credito_id uniqueidentifier  NULL,      -- claim; persiste siempre
        estado_erp          nvarchar(20)      NULL,      -- PorAutorizar
        resultado           tinyint           NULL,      -- NULL pendiente | 1 Aplicada | 2 Rechazada
        motivo              nvarchar(500)     NULL,
        procesada_at        datetimeoffset(3) NULL,

        CONSTRAINT PK_aw_solicitud_nota_credito PRIMARY KEY CLUSTERED (solicitud_id),
        CONSTRAINT UQ_asnc_nota_version UNIQUE (numero_nota, [version]),
        CONSTRAINT CK_asnc_tipo_nota    CHECK (tipo_nota IN ('Bonificacion', 'Devolucion')),
        CONSTRAINT CK_asnc_importe      CHECK (importe > 0),
        CONSTRAINT CK_asnc_resultado    CHECK (resultado IS NULL OR resultado IN (1, 2))
    );
    PRINT 'dbo.aw_solicitud_nota_credito creada.';
END
ELSE
    PRINT 'dbo.aw_solicitud_nota_credito ya existe — sin cambios.';
GO

/* Índice de barrido del ERP: WHERE resultado IS NULL ORDER BY numero_nota, version */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_asnc_barrido'
                 AND object_id = OBJECT_ID('dbo.aw_solicitud_nota_credito'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_asnc_barrido
        ON dbo.aw_solicitud_nota_credito (resultado, numero_nota, [version]);
    PRINT 'IX_asnc_barrido creado.';
END
GO

/* Permisos del login del ERP (creado en el script 04): lee y escribe solo su acuse.
   El permiso de INSERT para quien llene la tabla lo define TI. */
GRANT SELECT ON dbo.aw_solicitud_nota_credito TO millet_erp_integracion;
GRANT UPDATE (erp_nota_credito_id, estado_erp, resultado, motivo, procesada_at)
    ON dbo.aw_solicitud_nota_credito TO millet_erp_integracion;
GO

/* Rollback:
   -- DROP TABLE dbo.aw_solicitud_nota_credito;
*/
