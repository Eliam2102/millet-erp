/* =============================================================================
   19_create_erp_costo_promedio.sql — Costo promedio ERP → app de Almacenes (M-447, contrato v9).
   Ejecutar en: AlmacenRQ_MP_ENSAYO primero; AlmacenRQ_MP al arranque (cambiar USE). Idempotente.
   PROPUESTA: no se ejecuta sin visto bueno de TI (10-nov).

   Propósito:
   Publicar el costo promedio móvil que calcula el ERP para que la app de Almacenes lo lea en vez del
   costo de A+W. Misma base que dbo.alm_compra.

   Reglas:
   - El ERP solo INSERTA (como erp_tipo_cambio): una fila nueva por cada cambio del promedio, con version + 1.
     Nunca se modifica ni se borra.
   - Llave de almacén v9: sucursal + almacen_tipo + linea (ej. CONKAL + EXPORTACION + HEGLA 1). Herraje: linea = ''.
     Sucursal sin lista de almacenes: almacen_tipo = '' y linea = '' (el costo es el de la sucursal).
   - Vidrio: ancho_mm/alto_mm = medida de la hoja y costo_m2. Material por cantidad: ancho_mm/alto_mm NULL y costo_pieza.
   - Mismo costo para todos los lotes de esa medida en ese almacén. Con existencia cero se conserva la última fila.
   - moneda siempre 'PESOSMX'.
   - Idempotencia: el ERP no inserta dos veces la misma llave + version (índice único).
   ========================================================================== */

USE AlmacenRQ_MP_ENSAYO;  -- producción: AlmacenRQ_MP
GO

IF OBJECT_ID('dbo.erp_costo_promedio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.erp_costo_promedio
    (
        id             bigint            IDENTITY(1, 1) NOT NULL,
        articulo_aw    int               NOT NULL,          -- clave del artículo en A+W (igual que en los mensajes)
        sucursal       nvarchar(40)      NOT NULL,          -- clave A+W de la sucursal: CONKAL, CIRCUITO…
        almacen_tipo   nvarchar(60)      NOT NULL CONSTRAINT DF_ecp_tipo DEFAULT N'',   -- EXPORTACION, OBRAS… ('' sin lista)
        linea          nvarchar(60)      NOT NULL CONSTRAINT DF_ecp_linea DEFAULT N'',  -- HEGLA 1… ('' en herraje o sin lista)
        ancho_mm       int               NULL,              -- medida de hoja; NULL en material por cantidad
        alto_mm        int               NULL,
        costo_m2       decimal(18, 6)    NULL,              -- vidrio
        costo_pieza    decimal(18, 6)    NULL,              -- material por cantidad
        moneda         nvarchar(10)      NOT NULL,
        vigente_desde  datetime2(0)      NOT NULL,          -- UTC
        [version]      int               NOT NULL,          -- 1; +1 en cada cambio de la misma llave
        fecha_escrito  datetime2(0)      NOT NULL CONSTRAINT DF_ecp_fecha DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_erp_costo_promedio PRIMARY KEY CLUSTERED (id),
        CONSTRAINT CK_ecp_moneda   CHECK (moneda = N'PESOSMX'),
        CONSTRAINT CK_ecp_version  CHECK ([version] > 0),
        CONSTRAINT CK_ecp_costo    CHECK ((costo_m2 IS NOT NULL AND costo_pieza IS NULL AND ancho_mm > 0 AND alto_mm > 0)
                                       OR (costo_pieza IS NOT NULL AND costo_m2 IS NULL AND ancho_mm IS NULL AND alto_mm IS NULL)),
        CONSTRAINT CK_ecp_valor    CHECK (ISNULL(costo_m2, costo_pieza) >= 0)
    );
    PRINT 'dbo.erp_costo_promedio creada.';
END
ELSE
    PRINT 'dbo.erp_costo_promedio ya existe — sin cambios.';
GO

/* Una sola fila por llave + versión (la medida NULL cuenta como 0 para la unicidad). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_ecp_llave_version' AND object_id = OBJECT_ID('dbo.erp_costo_promedio'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_ecp_llave_version
        ON dbo.erp_costo_promedio (articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm, [version]);
    PRINT 'UX_ecp_llave_version creado.';
END
GO

/* Vista para la app: la versión más reciente de cada articulo/sucursal/almacen_tipo/linea/medida. */
CREATE OR ALTER VIEW dbo.vw_erp_costo_promedio_vigente AS
SELECT id, articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm, costo_m2, costo_pieza, moneda, vigente_desde, [version], fecha_escrito
FROM (
    SELECT c.*, ROW_NUMBER() OVER (
               PARTITION BY articulo_aw, sucursal, almacen_tipo, linea, ISNULL(ancho_mm, 0), ISNULL(alto_mm, 0)
               ORDER BY [version] DESC) AS rn
    FROM dbo.erp_costo_promedio c
) t
WHERE rn = 1;
GO

/* Permisos del ERP (login millet_erp_integracion), igual que alm_compra: agrega filas y consulta; nunca modifica ni borra. */
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT ON dbo.erp_costo_promedio TO millet_erp_integracion;
    DENY  UPDATE, DELETE ON dbo.erp_costo_promedio TO millet_erp_integracion;
END
GO

/* La app de Almacenes necesita SELECT sobre la vista (cuenta por definir con TI):
   GRANT SELECT ON dbo.vw_erp_costo_promedio_vigente TO <cuenta_app_almacenes>; */

/* Rollback (pruebas o desinstalación):
   -- DROP VIEW dbo.vw_erp_costo_promedio_vigente; DROP TABLE dbo.erp_costo_promedio; */
