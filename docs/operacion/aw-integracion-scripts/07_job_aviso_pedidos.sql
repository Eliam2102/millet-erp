/* =============================================================================
   07_job_aviso_pedidos.sql — Job de respaldo: aviso de pedidos en firme
   (U1.4 · ADR-0048 · doc integration/04 §2 · runbook de customizing §3.ter)

   Ejecutar en: MILLET_INTEGRACION (producción) o MILLET_INTEGRACION_DEV
   (pruebas): cambiar los USE y @base (sección 4); el nombre del job agrega
   « (DEV)» solo.
   Requiere: 02_create_table.sql. Compatible con SQL Server 2016 RTM (sin
   CREATE OR ALTER ni STRING_AGG). Idempotente.

   QUÉ HACE
   Aviso automático del pedido en firme: cada 15 s revisa A+W en SOLO
   LECTURA y escribe en dbo.aw_solicitud_pedido. Convive con el botón del
   vendedor, que escribe en la misma cola:

     Pedido en firme (STATUS 15 almacenable o 42 máquina; Módulo 13 R1):
       - nunca avisado               → operacion 1 (Alta)
       - ya dado de alta por el botón → operacion 2 (Modificación), nunca
         una segunda Alta
       - ya avisado y vuelve a 15/42 → operacion 2 (Modificación)
         (nuevo evento en BW_AUFTR_HIST posterior al último avisado)
     Pedidos internos Serie 9000 (ID 90000000–99999999): no se avisan.
     Pedido cancelado (STATUS 900 o 999; arquitectura A+W §4):
       - ya avisado (por el job o por el botón) y no cancelado
                                     → operacion 3 (Cancelación)
       - nunca avisado               → se ignora (el ERP no lo conoce)

   Reglas del contrato que respeta (runbook §4 y §6):
     - version estrictamente creciente por pedido: MAX(version)+1 dentro de
       la misma transacción, con UPDLOCK/HOLDLOCK para convivir con un
       customizing futuro sin chocar en UQ_asp_pedido_version.
     - Nunca UPDATE ni DELETE en la cola; nunca escribe columnas del ERP.
     - MILMAIN solo se LEE (Plano U1, regla 2). Lo propio del job vive en
       dos tablas de control de esta base: aw_aviso_job_config y
       aw_aviso_job_pedido.
     - Sin duplicados: un mismo evento (pedido + estatus + fecha de
       BW_AUFTR_HIST) produce a lo sumo un aviso.
     - Coexiste con el botón del vendedor y con el customizing: los tres
       escriben la MISMA cola y un pedido nunca se duplica. Si la cola ya
       tiene una solicitud del pedido creada en/después del evento (para
       cancelación, una cancelación), el job NO inserta; solo registra en
       aw_aviso_job_pedido el evento y la versión ya cubiertos.

   ARRANQUE: solo avisa eventos posteriores a aw_aviso_job_config.desde
   (se fija al instalar), para no inundar la cola con pedidos viejos que
   siguen en 15/42. Para incluir pedidos anteriores, bajar esa fecha.

   ORIGEN A+W: el job lee MILMAIN por sinónimos (dbo.aw_src_*). En el
   sandbox local (tools/aw-sandbox) se re-apuntan a AW_SANDBOX; en SER-DATA
   quedan en MILMAIN tal cual.

   LÍMITES CONOCIDOS:
     - Se basa en el STATUS actual de BW_AUFTR_KOPF: un pedido que pase por
       15 y salte a otro estatus en menos de 15 s no se avisa por este
       medio (el botón del vendedor lo cubre).
     - Un pedido cancelado (900/999) que vuelve a 15/42 se avisa como
       Modificación.
     - Lee MILMAIN con prioridad baja (DEADLOCK_PRIORITY LOW, LOCK_TIMEOUT
       5 s, READ UNCOMMITTED solo para la lectura de A+W).
   ========================================================================== */

USE MILLET_INTEGRACION;  -- repetir con MILLET_INTEGRACION_DEV
GO

/* ---------------------------------------------------------------------------
   1. Sinónimos al origen A+W (solo lectura)
   -------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.aw_src_auftr_kopf', 'SN') IS NULL
    CREATE SYNONYM dbo.aw_src_auftr_kopf FOR MILMAIN.SYSADM.BW_AUFTR_KOPF;
GO
IF OBJECT_ID('dbo.aw_src_auftr_hist', 'SN') IS NULL
    CREATE SYNONYM dbo.aw_src_auftr_hist FOR MILMAIN.SYSADM.BW_AUFTR_HIST;
GO

/* ---------------------------------------------------------------------------
   2. Tablas de control del job (propias de MILLET_INTEGRACION)
   -------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.aw_aviso_job_config', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.aw_aviso_job_config
    (
        id     tinyint  NOT NULL CONSTRAINT PK_aw_aviso_job_config PRIMARY KEY
                        CONSTRAINT CK_aajc_unica CHECK (id = 1),
        desde  datetime NOT NULL  -- solo eventos A+W posteriores a esta fecha
    );
    INSERT INTO dbo.aw_aviso_job_config (id, desde) VALUES (1, GETDATE());
    PRINT 'dbo.aw_aviso_job_config creada (desde = ahora).';
END
GO

IF OBJECT_ID('dbo.aw_aviso_job_pedido', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.aw_aviso_job_pedido
    (
        numero_pedido   nvarchar(50)      NOT NULL
            CONSTRAINT PK_aw_aviso_job_pedido PRIMARY KEY,
        ultimo_status   int               NOT NULL,  -- estatus A+W del último aviso
        ultimo_evento   datetime          NULL,      -- BW_AUFTR_HIST.DATUM avisado
        ultima_version  bigint            NOT NULL,
        avisado_at      datetimeoffset(3) NOT NULL
            CONSTRAINT DF_aajp_avisado_at DEFAULT SYSDATETIMEOFFSET()
    );
    PRINT 'dbo.aw_aviso_job_pedido creada.';
END
GO

/* ---------------------------------------------------------------------------
   3. Procedimiento: un barrido
   -------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.usp_aw_aviso_pedidos', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_aw_aviso_pedidos;
GO
CREATE PROCEDURE dbo.usp_aw_aviso_pedidos
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    /* MILMAIN es la base viva de A+W: el job cede ante cualquier bloqueo y
       no espera más de 5 s. Si no alcanza a leer, el siguiente barrido
       (15 s después) lo vuelve a intentar. */
    SET DEADLOCK_PRIORITY LOW;
    SET LOCK_TIMEOUT 5000;

    DECLARE @desde datetime = (SELECT desde FROM dbo.aw_aviso_job_config WHERE id = 1);
    IF @desde IS NULL
    BEGIN
        RAISERROR('Falta la fila de dbo.aw_aviso_job_config (id = 1).', 16, 1);
        RETURN;
    END;

    /* Candidatos: pedidos que HOY están en firme o cancelados. El filtro por
       STATUS usa IDX_BW_AUFKPF7 (no se filtra por fecha de captura: un pedido
       capturado hace días puede confirmarse hoy); los cancelados se acotan a los ya avisados
       (los 900/999 históricos son cientos de miles). El último evento del
       estatus actual sale de BW_AUFTR_HIST por IDX_BWAUFTR_HIST2 (ID, DATUM). */
    DECLARE @candidatos TABLE
    (
        numero_pedido nvarchar(50) NOT NULL PRIMARY KEY,
        [status]      int          NOT NULL,
        evento        datetime     NULL,
        operacion     tinyint      NULL
    );

    /* Lectura de MILMAIN sin tomar bloqueos compartidos: un estatus leído a
       medio cambio se corrige en el siguiente barrido. */
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    INSERT INTO @candidatos (numero_pedido, [status], evento)
    SELECT CAST(K.ID AS nvarchar(50)), K.STATUS,
           (SELECT MAX(H.DATUM) FROM dbo.aw_src_auftr_hist AS H
             WHERE H.ID = K.ID AND H.STATUS_ID = K.STATUS)
      FROM dbo.aw_src_auftr_kopf AS K
     WHERE K.STATUS IN (15, 42)
       AND K.ID NOT BETWEEN 90000000 AND 99999999;  -- Serie 9000: pedidos internos, no se facturan

    INSERT INTO @candidatos (numero_pedido, [status], evento)
    SELECT C.numero_pedido, K.STATUS,
           (SELECT MAX(H.DATUM) FROM dbo.aw_src_auftr_hist AS H
             WHERE H.ID = K.ID AND H.STATUS_ID = K.STATUS)
      FROM dbo.aw_aviso_job_pedido AS C
      JOIN dbo.aw_src_auftr_kopf   AS K ON K.ID = TRY_CAST(C.numero_pedido AS int)
     WHERE C.ultimo_status NOT IN (900, 999)
       AND K.STATUS IN (900, 999);

    /* Cancelaciones de pedidos que solo avisó otro escritor (botón del
       vendedor, customizing) y que el job nunca registró. */
    INSERT INTO @candidatos (numero_pedido, [status], evento)
    SELECT Q.numero_pedido, K.STATUS,
           (SELECT MAX(H.DATUM) FROM dbo.aw_src_auftr_hist AS H
             WHERE H.ID = K.ID AND H.STATUS_ID = K.STATUS)
      FROM (SELECT numero_pedido, operacion,
                   ROW_NUMBER() OVER (PARTITION BY numero_pedido ORDER BY [version] DESC) AS rn
              FROM dbo.aw_solicitud_pedido) AS Q
      JOIN dbo.aw_src_auftr_kopf AS K ON K.ID = TRY_CAST(Q.numero_pedido AS int)
     WHERE Q.rn = 1 AND Q.operacion < 3
       AND K.STATUS IN (900, 999)
       AND NOT EXISTS (SELECT 1 FROM dbo.aw_aviso_job_pedido AS C WHERE C.numero_pedido = Q.numero_pedido)
       AND NOT EXISTS (SELECT 1 FROM @candidatos AS X WHERE X.numero_pedido = Q.numero_pedido);

    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

    /* Clasificación (ver encabezado). Un pedido en 15/42 sin evento en el
       historial no se puede fechar contra @desde: no se avisa (A+W registra
       cada cambio de estatus en BW_AUFTR_HIST; si falta, es un caso a
       revisar con Jorge, no a adivinar). */
    UPDATE X
       SET operacion =
           CASE
             WHEN X.[status] IN (900, 999) THEN 3
             WHEN C.numero_pedido IS NULL
                  AND X.evento >= @desde THEN 1
             WHEN C.numero_pedido IS NOT NULL
                  AND X.evento > ISNULL(C.ultimo_evento, '19000101') THEN 2
           END
      FROM @candidatos AS X
      LEFT JOIN dbo.aw_aviso_job_pedido AS C ON C.numero_pedido = X.numero_pedido;

    DELETE FROM @candidatos WHERE operacion IS NULL;

    /* Un aviso por pedido, cada uno en su transacción: si uno falla, los
       demás siguen y el fallido se reintenta en el siguiente barrido. */
    DECLARE @np nvarchar(50), @st int, @ev datetime, @op tinyint, @ver bigint, @cubierto int, @ultv bigint;
    DECLARE @avisos int = 0;

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT numero_pedido, [status], evento, operacion FROM @candidatos
         ORDER BY ISNULL(evento, @desde), numero_pedido;
    OPEN cur;
    FETCH NEXT FROM cur INTO @np, @st, @ev, @op;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;

            /* @cubierto: otro escritor (botón del vendedor, customizing) ya
               dejó en la cola una solicitud posterior a este evento — para
               cancelación, una cancelación; para alta o modificación, una
               alta o modificación. Solo cuentan las filas que el job no
               escribió (versión mayor a la última suya). creada_at es
               datetimeoffset: se compara su hora local contra la fecha local
               de A+W. */
            SET @ultv = ISNULL((SELECT ultima_version FROM dbo.aw_aviso_job_pedido
                                 WHERE numero_pedido = @np), 0);
            /* El botón del vendedor ya dio de alta el pedido (antes de 15/42):
               el job no manda otra Alta, manda Modificación para que el ERP
               tome lo que cambió hasta el firme. */
            IF @op = 1 AND EXISTS (SELECT 1 FROM dbo.aw_solicitud_pedido
                                    WHERE numero_pedido = @np AND [version] > @ultv AND operacion < 3)
                SET @op = 2;
            SELECT @ver = ISNULL(MAX([version]), 0) + 1,
                   @cubierto = ISNULL(MAX(CASE
                       WHEN [version] > @ultv
                            AND CAST(creada_at AS datetime) >= ISNULL(@ev, @desde)
                            AND ((@op < 3 AND operacion < 3) OR (@op = 3 AND operacion = 3)) THEN 1 END), 0)
              FROM dbo.aw_solicitud_pedido WITH (UPDLOCK, HOLDLOCK)
             WHERE numero_pedido = @np;

            IF @cubierto = 1
                SET @ver -= 1;  -- el control del job recuerda la versión ya cubierta
            ELSE
                INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
                VALUES (@np, @op, @ver);

            IF EXISTS (SELECT 1 FROM dbo.aw_aviso_job_pedido WHERE numero_pedido = @np)
                UPDATE dbo.aw_aviso_job_pedido
                   SET ultimo_status = @st, ultimo_evento = @ev,
                       ultima_version = @ver, avisado_at = SYSDATETIMEOFFSET()
                 WHERE numero_pedido = @np;
            ELSE
                INSERT INTO dbo.aw_aviso_job_pedido
                    (numero_pedido, ultimo_status, ultimo_evento, ultima_version)
                VALUES (@np, @st, @ev, @ver);

            COMMIT TRANSACTION;
            IF @cubierto = 0 SET @avisos += 1;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            DECLARE @msg nvarchar(2048) =
                N'aviso de pedido ' + @np + N' falló: ' + ERROR_MESSAGE();
            RAISERROR(@msg, 10, 1) WITH NOWAIT;  -- informativo: no corta el barrido
        END CATCH;

        FETCH NEXT FROM cur INTO @np, @st, @ev, @op;
    END;
    CLOSE cur;
    DEALLOCATE cur;

    IF @avisos > 0
        PRINT CONCAT('usp_aw_aviso_pedidos: ', @avisos, ' aviso(s) insertado(s).');
END
GO

/* ---------------------------------------------------------------------------
   4. Job de SQL Server Agent: cada 15 s (mínimo que permite el Agent: 10 s)
   Requiere SQL Server Agent activo en SER-DATA\AWBUSINESS y permiso de
   Jorge. Para la base de pruebas: cambiar @base a MILLET_INTEGRACION_DEV;
   el nombre del job agrega « (DEV)» solo, para que pruebas y producción
   puedan convivir en el mismo Agent.
   -------------------------------------------------------------------------*/
USE msdb;
GO
DECLARE @base sysname = N'MILLET_INTEGRACION';
DECLARE @job  sysname = N'Millet ERP - aviso de pedidos A+W'
                      + CASE WHEN @base LIKE N'%[_]DEV' THEN N' (DEV)' ELSE N'' END;

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @job)
    PRINT 'El job ya existe — sin cambios.';
ELSE
BEGIN
    EXEC msdb.dbo.sp_add_job
         @job_name = @job,
         @description = N'U1.4: inserta en aw_solicitud_pedido los pedidos que pasan a 15/42 o se cancelan (900/999). Respaldo del customizing.',
         @enabled = 1;
    EXEC msdb.dbo.sp_add_jobstep
         @job_name = @job,
         @step_name = N'usp_aw_aviso_pedidos',
         @subsystem = N'TSQL',
         @database_name = @base,
         @command = N'EXEC dbo.usp_aw_aviso_pedidos;';
    EXEC msdb.dbo.sp_add_jobschedule
         @job_name = @job,
         @name = N'Cada 15 segundos',
         @freq_type = 4,             -- diario
         @freq_interval = 1,
         @freq_subday_type = 2,      -- segundos
         @freq_subday_interval = 15,
         @active_start_time = 0;
    EXEC msdb.dbo.sp_add_jobserver @job_name = @job;
    PRINT 'Job creado: ' + @job;
END
GO

/* Rollback. Se pone la base UNA vez y el nombre del job sale de ella, igual que al crearlo,
   para que quitar el de pruebas nunca borre el de producción (observación de TI, 26-oct):
   DECLARE @base sysname = N'MILLET_INTEGRACION_DEV';   -- producción: N'MILLET_INTEGRACION'
   DECLARE @job  sysname = N'Millet ERP - aviso de pedidos A+W'
                         + CASE WHEN @base LIKE N'%[_]DEV' THEN N' (DEV)' ELSE N'' END;
   EXEC msdb.dbo.sp_delete_job @job_name = @job;
   EXEC (N'USE ' + QUOTENAME(@base) + N';
     DROP PROCEDURE dbo.usp_aw_aviso_pedidos;
     DROP TABLE dbo.aw_aviso_job_pedido; DROP TABLE dbo.aw_aviso_job_config;
     DROP SYNONYM dbo.aw_src_auftr_hist; DROP SYNONYM dbo.aw_src_auftr_kopf;');
   (La cola no se toca: sus filas son bitácora de correlación.)
*/
