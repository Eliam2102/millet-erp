/* =============================================================================
   14_create_aw_retorno.sql — Tabla de retorno ERP → A+W (aw_retorno), v2
   Ejecutar en: MILLET_INTEGRACION_DEV primero; MILLET_INTEGRACION al arranque
   (cambiar USE). Idempotente. PROPUESTA: no se ejecuta sin visto bueno de TI.

   Contrato: borrador «Pedidos en firme A+W», Anexo B2, con los cuatro ajustes
   que pidió TI de Millet el 26-oct (contestación del 27-oct, punto 10):
     a) version_pedido: versión del pedido (aw_solicitud_pedido.version) que el
        ERP tenía al calcular la fila. Si al aplicar A+W ya tiene una versión
        mayor, TI NO aplica y marca resultado = 3 (desfasada); el ERP recalcula
        y deja una fila nueva.
     b) origen + usuario_libera: la liberación (70) dice si fue automática por
        saldo $0 o manual de CxC, y quién la autorizó cuando es manual.
     c) intentos: TI suma 1 en cada intento rechazado. Procedimiento propuesto:
        se reintenta en cada pasada hasta 3 intentos; con 3 o más la fila queda
        en resultado = 2 y la revisa una persona (consulta al final).
     d) secuencia única por pedido sin importar el tipo: TI aplica estrictamente
        por (numero_pedido, secuencia). Así el saldo (tipo 2) nunca se aplica
        después de un 115 que lo supone.

   Reglas (sin cambio desde B2): las filas no se editan ni se borran (un cambio
   es una fila nueva); la liberación es siempre total (TI, 26-oct).
   El ERP escribe todo salvo aplicada_at, resultado, motivo e intentos, que
   escribe solo la tarea de TI.

   Estatus que devuelve el ERP: 70 (liberado) y 115 (facturado y entregado).
   El 71 no: lo pone A+W al emitir la hoja de salida, y el 72 el programa de
   salidas (TI, 26-oct, pregunta 1).
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;  -- producción: MILLET_INTEGRACION
GO

IF OBJECT_ID('dbo.aw_retorno', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.aw_retorno
    (
        retorno_id          uniqueidentifier  NOT NULL
            CONSTRAINT DF_awr_retorno_id DEFAULT NEWID(),
        numero_pedido       nvarchar(50)      NOT NULL,
        secuencia           int               NOT NULL,  -- 1, 2, 3… por pedido, sin importar el tipo (d)
        tipo                tinyint           NOT NULL,  -- 1 Estatus | 2 Saldo | 3 Documento
        version_pedido      bigint            NOT NULL,  -- (a)

        -- tipo 1
        estatus_destino     smallint          NULL,      -- 70 | 115
        origen              tinyint           NULL,      -- (b) 1 Automatico (saldo $0) | 2 Manual CxC; solo con 70
        usuario_libera      nvarchar(100)     NULL,      -- (b) obligatorio si origen = 2

        -- tipo 2
        anticipos_aplicados decimal(18, 2)    NULL,
        saldo               decimal(18, 2)    NULL,      -- 0 = liquidado

        -- tipo 3 y estatus 115
        tipo_documento      nvarchar(20)      NULL,      -- FacturaFinal | NotaCredito
        folio               nvarchar(30)      NULL,
        [uuid]              nvarchar(36)      NULL,
        importe             decimal(18, 2)    NULL,

        creada_at           datetimeoffset(3) NOT NULL
            CONSTRAINT DF_awr_creada_at DEFAULT SYSDATETIMEOFFSET(),

        -- ===== los escribe SOLO la tarea de TI =====
        aplicada_at         datetimeoffset(3) NULL,
        resultado           tinyint           NULL,      -- NULL pendiente | 1 Aplicada | 2 Rechazada | 3 Desfasada
        motivo              nvarchar(500)     NULL,
        intentos            int               NOT NULL
            CONSTRAINT DF_awr_intentos DEFAULT 0,        -- (c)

        CONSTRAINT PK_aw_retorno            PRIMARY KEY CLUSTERED (retorno_id),
        CONSTRAINT UQ_awr_pedido_secuencia  UNIQUE (numero_pedido, secuencia),
        CONSTRAINT CK_awr_tipo              CHECK (tipo IN (1, 2, 3)),
        CONSTRAINT CK_awr_resultado         CHECK (resultado IS NULL OR resultado IN (1, 2, 3)),
        CONSTRAINT CK_awr_intentos          CHECK (intentos >= 0),
        CONSTRAINT CK_awr_estatus           CHECK (tipo <> 1 OR ISNULL(estatus_destino, 0) IN (70, 115)),
        CONSTRAINT CK_awr_origen            CHECK (
            (ISNULL(estatus_destino, 0) = 70 AND ISNULL(origen, 0) IN (1, 2)) OR (ISNULL(estatus_destino, 0) <> 70 AND origen IS NULL)),
        CONSTRAINT CK_awr_usuario_libera    CHECK (origen <> 2 OR usuario_libera IS NOT NULL),
        CONSTRAINT CK_awr_saldo             CHECK (tipo <> 2 OR saldo IS NOT NULL),
        CONSTRAINT CK_awr_documento         CHECK (tipo <> 3 OR ([uuid] IS NOT NULL AND tipo_documento IN (N'FacturaFinal', N'NotaCredito')))
    );
    PRINT 'dbo.aw_retorno creada.';
END
ELSE
    PRINT 'dbo.aw_retorno ya existe — sin cambios.';
GO

/* Barrido de la tarea de TI: pendientes y rechazadas con menos de 3 intentos,
   en orden estricto por pedido y secuencia (d). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_awr_barrido' AND object_id = OBJECT_ID('dbo.aw_retorno'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_awr_barrido
        ON dbo.aw_retorno (resultado, numero_pedido, secuencia)
        INCLUDE (tipo, version_pedido, intentos);
    PRINT 'IX_awr_barrido creado.';
END
GO

/* Permisos del ERP (usuario del script 04): agrega filas y lee el resultado.
   No puede actualizar ni borrar. */
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT ON dbo.aw_retorno TO millet_erp_integracion;
    DENY  UPDATE, DELETE ON dbo.aw_retorno TO millet_erp_integracion;
END
GO

/* Permisos de la tarea de TI que aplica el retorno (usuario por definir con TI;
   la contraseña la crea TI y no viaja por chat ni correo):
   GRANT SELECT ON dbo.aw_retorno TO <usuario_tarea_ti>;
   GRANT UPDATE (aplicada_at, resultado, motivo, intentos) ON dbo.aw_retorno TO <usuario_tarea_ti>;
*/

/* Qué aplicar en la siguiente pasada (tarea de TI): la primera fila no aplicada
   de cada pedido; si está rechazada con 3 intentos o más, el pedido se detiene
   ahí hasta que lo revise una persona.
   SELECT r.*
   FROM dbo.aw_retorno AS r
   WHERE r.secuencia = (SELECT MIN(x.secuencia) FROM dbo.aw_retorno AS x
                        WHERE x.numero_pedido = r.numero_pedido
                          AND ISNULL(x.resultado, 0) NOT IN (1, 3))
     AND r.intentos < 3
   ORDER BY r.numero_pedido, r.secuencia;

   Para revisión de una persona:
   SELECT * FROM dbo.aw_retorno WHERE resultado = 2 AND intentos >= 3;
*/

/* Rollback (en pruebas):
   -- DROP TABLE dbo.aw_retorno;
*/
