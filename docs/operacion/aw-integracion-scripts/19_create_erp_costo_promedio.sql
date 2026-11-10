/* =============================================================================
   19_create_erp_costo_promedio.sql — Costo promedio del ERP para la app de Almacenes
   (contrato v9, M-447 / 86e3zr7au).
   Ejecutar en: AlmacenRQ_MP_ENSAYO primero (pruebas); AlmacenRQ_MP al arranque.
   Idempotente: se puede correr dos veces. PROPUESTA: no se ejecuta sin visto bueno de TI.
   Vista vía stub (CREATE mínimo si no existe) + ALTER, como el script 03: NO usar CREATE OR ALTER,
   el servidor es SQL Server 2016 RTM sin SP1 (13.0.1601.5) y CREATE OR ALTER requiere SP1.

   Propósito:
   La app de Almacenes valúa ENTRADA_MANUAL y SALIDA_MANUAL con el costo promedio del ERP.
   El ERP publica aquí su promedio por artículo + sucursal + almacén (tipo + línea) + medida.
   La app lo lee de la vista dbo.vw_erp_costo_promedio_vigente.

   Reglas:
   - Unidad: vidrio con medida = costo_m2 (pesos por m²); sin medida = costo_pieza
     (pesos por unidad del artículo). Siempre moneda = PESOSMX.
   - Llave: articulo_aw (clave A+W, la misma de los mensajes) + sucursal (clave A+W, p. ej. CONKAL)
     + almacen_tipo + linea + ancho_mm + alto_mm.
     * linea vacía ('') en herraje.
     * almacen_tipo y linea vacíos en sucursales sin lista de almacenes: costo de la sucursal.
     * ancho_mm y alto_mm NULL en material sin medida.
     * Medida sin orientación: ancho_mm es el lado menor y alto_mm el mayor (2600×1800 y
       1800×2600 son la misma llave: ancho_mm = 1800, alto_mm = 2600).
   - Inmutable: el ERP solo inserta. Un cambio de promedio es una fila nueva con version + 1.
   - Con existencia en cero el ERP no publica nada: la última fila sigue vigente.
   - Sin fila: el ERP nunca tuvo ese artículo en ese almacén.
   - Seguridad: el ERP (millet_erp_integracion) solo tiene INSERT y SELECT; se niegan UPDATE y DELETE.
   ========================================================================== */

USE AlmacenRQ_MP_ENSAYO;  -- producción: AlmacenRQ_MP
GO

SET NOEXEC OFF;
IF DB_NAME() NOT IN (N'AlmacenRQ_MP', N'AlmacenRQ_MP_ENSAYO')
BEGIN
    RAISERROR('Base "%s" incorrecta: este script va en AlmacenRQ_MP o AlmacenRQ_MP_ENSAYO.', 16, 1, DB_NAME());
    SET NOEXEC ON;  -- un RETURN solo aborta el batch; NOEXEC detiene los batches siguientes
END
GO

IF OBJECT_ID('dbo.erp_costo_promedio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.erp_costo_promedio
    (
        id            bigint IDENTITY(1, 1) NOT NULL,
        articulo_aw   int               NOT NULL,  -- clave del artículo en A+W
        sucursal      nvarchar(30)      NOT NULL,  -- clave A+W de la sucursal (CONKAL)
        almacen_tipo  nvarchar(50)      NOT NULL CONSTRAINT DF_ecp_tipo  DEFAULT N'',  -- EXPORTACION, OBRAS, GENERAL...
        linea         nvarchar(50)      NOT NULL CONSTRAINT DF_ecp_linea DEFAULT N'',  -- HEGLA 1, BYSTRONIC...; '' en herraje
        ancho_mm      int               NULL,      -- medida de la hoja; NULL sin medida
        alto_mm       int               NULL,
        costo_m2      decimal(19, 6)    NULL,      -- pesos por m² (con medida)
        costo_pieza   decimal(19, 6)    NULL,      -- pesos por unidad (sin medida)
        moneda        nvarchar(10)      NOT NULL CONSTRAINT DF_ecp_moneda DEFAULT N'PESOSMX',
        vigente_desde datetime2(0)      NOT NULL,  -- UTC: cuándo cambió el promedio en el ERP
        [version]     bigint            NOT NULL,  -- 1, 2, 3... por llave
        creada_at     datetime2(0)      NOT NULL CONSTRAINT DF_ecp_creada DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_erp_costo_promedio PRIMARY KEY CLUSTERED (id),
        CONSTRAINT CK_ecp_moneda   CHECK (moneda = N'PESOSMX'),
        CONSTRAINT CK_ecp_version  CHECK ([version] > 0),
        CONSTRAINT CK_ecp_medida   CHECK ((ancho_mm IS NULL AND alto_mm IS NULL) OR (ancho_mm > 0 AND alto_mm > 0)),
        CONSTRAINT CK_ecp_costo    CHECK (
            (ancho_mm IS NOT NULL AND costo_m2 IS NOT NULL AND costo_pieza IS NULL AND costo_m2 >= 0)
         OR (ancho_mm IS NULL AND costo_pieza IS NOT NULL AND costo_m2 IS NULL AND costo_pieza >= 0))
    );
    PRINT 'dbo.erp_costo_promedio creada.';
END
ELSE
    PRINT 'dbo.erp_costo_promedio ya existe — sin cambios.';
GO

/* Idempotencia: una sola fila por llave + versión. En SQL Server un índice único trata los NULL
   como iguales, así que también protege el material sin medida. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_ecp_llave_version' AND object_id = OBJECT_ID('dbo.erp_costo_promedio'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_ecp_llave_version
        ON dbo.erp_costo_promedio (articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm, [version] DESC)
        INCLUDE (costo_m2, costo_pieza, vigente_desde);
    PRINT 'UX_ecp_llave_version creado.';
END
GO

/* Vista que lee la app: la última versión de cada llave. */
IF OBJECT_ID('dbo.vw_erp_costo_promedio_vigente', 'V') IS NULL
    EXEC (N'CREATE VIEW dbo.vw_erp_costo_promedio_vigente AS SELECT 1 AS stub');
GO

ALTER VIEW dbo.vw_erp_costo_promedio_vigente
AS
SELECT articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm,
       costo_m2, costo_pieza, moneda, vigente_desde, [version]
  FROM (SELECT c.*,
               ROW_NUMBER() OVER (PARTITION BY articulo_aw, sucursal, almacen_tipo, linea, ancho_mm, alto_mm
                                  ORDER BY [version] DESC) AS rn
          FROM dbo.erp_costo_promedio c) x
 WHERE x.rn = 1;
GO
PRINT 'dbo.vw_erp_costo_promedio_vigente lista.';
GO

/* Permisos del ERP (millet_erp_integracion): inserta y lee; nunca modifica ni borra. */
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT ON dbo.erp_costo_promedio TO millet_erp_integracion;
    DENY  UPDATE, DELETE ON dbo.erp_costo_promedio TO millet_erp_integracion;
    GRANT SELECT ON dbo.vw_erp_costo_promedio_vigente TO millet_erp_integracion;
    PRINT 'Permisos de millet_erp_integracion aplicados.';
END
ELSE
    PRINT 'AVISO: no existe el usuario millet_erp_integracion en esta base; permisos pendientes.';
GO

/* Lectura de la app (TI: poner el usuario con que la app ya entra a esta base y quitar el comentario):
   GRANT SELECT ON dbo.vw_erp_costo_promedio_vigente TO <usuario_app_almacenes>;
*/

/* Consulta sugerida para la app (entrada o salida manual de una hoja):
   SELECT costo_m2, costo_pieza, moneda, vigente_desde
     FROM dbo.vw_erp_costo_promedio_vigente
    WHERE articulo_aw = @articuloAw AND sucursal = @sucursal
      AND almacen_tipo = @tipo AND linea = @linea          -- '' en herraje
      AND ((ancho_mm IS NULL AND @ancho IS NULL)                     -- sin medida
           OR (ancho_mm = CASE WHEN @ancho <= @alto THEN @ancho ELSE @alto END   -- lado menor
               AND alto_mm = CASE WHEN @ancho <= @alto THEN @alto ELSE @ancho END)); -- lado mayor
   Sin fila: el ERP no tiene costo para esa llave.
*/

SET NOEXEC OFF;
GO

/* Rollback (pruebas o desinstalación):
   -- DROP VIEW dbo.vw_erp_costo_promedio_vigente;
   -- DROP TABLE dbo.erp_costo_promedio;
*/
