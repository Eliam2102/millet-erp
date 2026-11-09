/* =============================================================================
   18_create_erp_tipo_cambio.sql — Tabla puente ERP → A+W de Tipo de Cambio (I13.2 / 86e3kdvfz).
   Ejecutar en: MILLET_INTEGRACION_DEV primero; MILLET_INTEGRACION al arranque
   (cambiar USE). Idempotente. PROPUESTA: no se ejecuta sin visto bueno de TI.

   Propósito (flujo 14 / I13.2):
   Publicar el Tipo de Cambio diario calculado por el ERP (K10.6: escalera Banxico FIX
   -> DOF -> fecha de aplicación) para que la tarea programada de TI de Millet lo aplique
   en A+W (SYSADM.KA_WAEHRUNGEN) al apagarse SAP.

   Reglas del flujo:
   - Monedas: USD, Euro y CAN: son los valores propios de A+W (SYSADM.KA_WAEHRUNGEN.WAEHRUNG),
     no códigos ISO (respuesta de TI, 5-nov). El ERP traduce con IntegracionesAw:MapeoMonedaAw
     (USD->USD, EUR->Euro, CAD->CAN). El cruce con A+W es por WAEHRUNG (FREMD_KEY trae el ISO, pero no es la llave).
   - Inmutabilidad: las filas NO se modifican ni se borran por el ERP. Un cambio en el
     valor (corrección DOF/Banxico o captura manual) genera una fila con version + 1.
   - Quien escribe qué:
     * El ERP escribe: moneda, fecha_aplicacion, version, valor, fecha_fix, origen, creada_at.
     * La tarea de TI escribe: aplicada_at, resultado, motivo, intentos.
   - Seguridad: el ERP solo tiene permisos de INSERT y SELECT; se le deniegan UPDATE y DELETE.
   - Idempotencia: llave primaria (moneda, fecha_aplicacion, version).
   - Resultado de TI:
     * NULL = Pendiente
     * 1    = Aplicada exitosamente en A+W
     * 2    = Rechazada (error técnico o inconsistencia)
     * 3    = Omitida (superada por una versión mayor de la misma moneda y fecha)
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;  -- producción: MILLET_INTEGRACION
GO

IF OBJECT_ID('dbo.erp_tipo_cambio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.erp_tipo_cambio
    (
        moneda            nvarchar(8)       NOT NULL,  -- WAEHRUNG de A+W: USD | Euro | CAN
        fecha_aplicacion  date              NOT NULL,  -- día en que rige el TC (escalera K10.6, incluye inhábiles)
        [version]         int               NOT NULL,  -- 1; +1 si el ERP corrige el valor de esa fecha
        valor             decimal(18, 6)    NOT NULL,  -- pesos MXN por unidad de moneda extranjera
        fecha_fix         date              NULL,      -- fecha del FIX de origen para rastreo; NULL si es manual
        origen            nvarchar(10)      NOT NULL,  -- Banxico | Manual
        creada_at         datetimeoffset(3) NOT NULL
            CONSTRAINT DF_etc_creada_at DEFAULT SYSDATETIMEOFFSET(),

        -- ===== los escribe SOLO la tarea de TI (acuse) =====
        aplicada_at       datetimeoffset(3) NULL,
        resultado         tinyint           NULL,      -- NULL pendiente | 1 Aplicada | 2 Rechazada | 3 Omitida
        motivo            nvarchar(500)     NULL,
        intentos          int               NOT NULL
            CONSTRAINT DF_etc_intentos DEFAULT 0,

        CONSTRAINT PK_erp_tipo_cambio PRIMARY KEY CLUSTERED (moneda, fecha_aplicacion, [version]),
        CONSTRAINT CK_etc_moneda      CHECK (moneda IN (N'USD', N'Euro', N'CAN')),
        CONSTRAINT CK_etc_version     CHECK ([version] > 0),
        CONSTRAINT CK_etc_valor       CHECK (valor > 0),
        CONSTRAINT CK_etc_origen      CHECK (origen IN (N'Banxico', N'Manual')),
        CONSTRAINT CK_etc_resultado   CHECK (resultado IS NULL OR resultado IN (1, 2, 3)),
        CONSTRAINT CK_etc_intentos    CHECK (intentos >= 0)
    );
    PRINT 'dbo.erp_tipo_cambio creada.';
END
ELSE
    PRINT 'dbo.erp_tipo_cambio ya existe — sin cambios.';
GO

/* Índice de barrido para consulta de TI y monitor del ERP:
   Permite ubicar rápidamente la versión más reciente de cada moneda para una fecha de aplicación dada. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_etc_barrido' AND object_id = OBJECT_ID('dbo.erp_tipo_cambio'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_etc_barrido
        ON dbo.erp_tipo_cambio (fecha_aplicacion, moneda, [version] DESC)
        INCLUDE (valor, resultado, intentos, aplicada_at);
    PRINT 'IX_etc_barrido creado.';
END
GO

/* Permisos del ERP (usuario millet_erp_integracion):
   Agrega filas (INSERT) y consulta el estado del acuse (SELECT).
   Se deniegan estrictamente UPDATE y DELETE para garantizar inmutabilidad. */
IF DATABASE_PRINCIPAL_ID('millet_erp_integracion') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT ON dbo.erp_tipo_cambio TO millet_erp_integracion;
    DENY  UPDATE, DELETE ON dbo.erp_tipo_cambio TO millet_erp_integracion;
END
GO

/* Permisos sugeridos para la tarea de TI:
   La cuenta de servicio de TI que aplica el valor en A+W necesita leer la tabla
   y actualizar exclusivamente las columnas de acuse:
   GRANT SELECT ON dbo.erp_tipo_cambio TO <cuenta_servicio_ti>;
   GRANT UPDATE (aplicada_at, resultado, motivo, intentos) ON dbo.erp_tipo_cambio TO <cuenta_servicio_ti>;
*/

/* Consulta sugerida para la tarea de TI:
   Cruce con A+W: la fila de KA_WAEHRUNGEN es la que cumple WAEHRUNG = t.moneda (un solo valor
   vigente por moneda: VALUTAKURS + VALUTADATUM). Llave = WAEHRUNG (colación CS_AS: 'Euro', 'CAN' exactos).
   OJO: valor del ERP = MXN por unidad extranjera; A+W guarda el INVERSO:
   VALUTAKURS = ROUND(1.0 / t.valor, 8) (decimal >= 8, half-up). EUR 0.04804228 = 20.81 MXN/EUR.
   Obtener el registro vigente a aplicar para la fecha actual (hora México):
   SELECT t.*
   FROM dbo.erp_tipo_cambio t
   INNER JOIN (
       SELECT moneda, fecha_aplicacion, MAX([version]) AS max_version
       FROM dbo.erp_tipo_cambio
       WHERE fecha_aplicacion = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Central Standard Time (Mexico)' AS date)
       GROUP BY moneda, fecha_aplicacion
   ) m ON t.moneda = m.moneda AND t.fecha_aplicacion = m.fecha_aplicacion AND t.[version] = m.max_version
   WHERE (t.resultado IS NULL OR (t.resultado = 2 AND t.intentos < 3));

   Actualización del acuse por TI al aplicar exitosamente:
   UPDATE dbo.erp_tipo_cambio
   SET aplicada_at = SYSDATETIMEOFFSET(),
       resultado   = 1,
       motivo      = NULL
   WHERE moneda = @moneda AND fecha_aplicacion = @fecha AND [version] = @version;

   Marcado de versiones obsoletas/superadas:
   UPDATE dbo.erp_tipo_cambio
   SET aplicada_at = SYSDATETIMEOFFSET(),
       resultado   = 3,
       motivo      = N'Superada por versión mayor'
   WHERE moneda = @moneda AND fecha_aplicacion = @fecha AND [version] < @version AND resultado IS NULL;
*/

/* Rollback (en caso de pruebas o desinstalación):
   -- DROP TABLE dbo.erp_tipo_cambio;
*/
