-- U1.4 · escenarios del job de aviso (07_job_aviso_pedidos.sql) sobre el sandbox A+W.
-- Lo ejecuta u14-job-aviso.sh por fases ($(FASE) = 1 a 8). Pedidos de prueba 990001–990009.
-- Requiere los defaults temporales del seed (03-defaults-temporales.sql) para insertar en KOPF/HIST.
SET NOCOUNT ON;
USE AW_SANDBOX;
DECLARE @fase int = $(FASE);
DECLARE @desde datetime = (SELECT desde FROM MILLET_INTEGRACION.dbo.aw_aviso_job_config WHERE id = 1);

IF @fase = 1
BEGIN
    -- Limpieza de corridas anteriores (solo pedidos de prueba).
    DELETE FROM SYSADM.BW_AUFTR_HIST WHERE ID BETWEEN 990001 AND 990009;
    DELETE FROM SYSADM.BW_AUFTR_KOPF WHERE ID BETWEEN 990001 AND 990009;
    DELETE FROM SYSADM.BW_AUFTR_HIST WHERE ID = 90990001;
    DELETE FROM SYSADM.BW_AUFTR_KOPF WHERE ID = 90990001;

    -- 990001 hojas pasa a 15 (CA13.1) · 990002 máquina pasa a 42 (CA13.2)
    -- 990003 pasa a 15 (se modificará y cancelará después)
    -- 990004 cancelado sin aviso previo → se ignora
    -- 990005 en 15 desde ANTES de activar el job → no se avisa (arranque)
    -- 990006 en 15 sin evento en el historial → no se avisa
    -- 90990001 Serie 9000 (pedido interno) en 15 → no se avisa
    INSERT INTO SYSADM.BW_AUFTR_KOPF (ID, STATUS, DATUM_ERF) VALUES
        (990001, 15, CAST(GETDATE() AS date)), (990002, 42, CAST(GETDATE() AS date)),
        (990003, 15, CAST(GETDATE() AS date)), (990004, 900, CAST(GETDATE() AS date)),
        (990005, 15, DATEADD(day, -3, CAST(GETDATE() AS date))), (990006, 15, CAST(GETDATE() AS date)),
        (90990001, 15, CAST(GETDATE() AS date));
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID) VALUES
        (990001, 15, GETDATE(), N'u14', NEWID()), (990002, 42, GETDATE(), N'u14', NEWID()),
        (990003, 15, GETDATE(), N'u14', NEWID()), (990004, 900, GETDATE(), N'u14', NEWID()),
        (990005, 15, DATEADD(day, -3, @desde), N'u14', NEWID()),
        (90990001, 15, GETDATE(), N'u14', NEWID());
    SELECT 'fase 1: pedidos en A+W' AS paso, CONVERT(varchar(23), GETDATE(), 121) AS en;
END
ELSE IF @fase = 2
BEGIN
    -- 990003 se modifica: A+W lo regresa a captura y lo vuelve a confirmar (nuevo evento 15)
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID)
    VALUES (990003, 15, DATEADD(second, 1, GETDATE()), N'u14', NEWID());
    SELECT 'fase 2: 990003 reconfirmado' AS paso, CONVERT(varchar(23), GETDATE(), 121) AS en;
END
ELSE IF @fase = 3
BEGIN
    -- 990003 se cancela en A+W (CA13.3)
    UPDATE SYSADM.BW_AUFTR_KOPF SET STATUS = 900 WHERE ID = 990003;
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID)
    VALUES (990003, 900, GETDATE(), N'u14', NEWID());
    SELECT 'fase 3: 990003 cancelado' AS paso, CONVERT(varchar(23), GETDATE(), 121) AS en;
END
ELSE IF @fase = 5
BEGIN
    -- 990007: el vendedor pulsa el botón ANTES de que corra el job. Evento 15 en A+W y luego el
    -- INSERT del botón en la cola, en una sola transacción para que el Agent no los vea por separado.
    -- Esperado: el job no inserta nada (una sola fila, la del botón).
    BEGIN TRANSACTION;
    INSERT INTO SYSADM.BW_AUFTR_KOPF (ID, STATUS, DATUM_ERF) VALUES (990007, 15, CAST(GETDATE() AS date));
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID) VALUES (990007, 15, GETDATE(), N'u14', NEWID());
    WAITFOR DELAY '00:00:00.100';
    INSERT INTO MILLET_INTEGRACION.dbo.aw_solicitud_pedido (numero_pedido, operacion, [version]) VALUES (N'990007', 1, 1);
    COMMIT;
    SELECT 'fase 5: 990007 con aviso del botón antes del job' AS paso, CONVERT(varchar(23), GETDATE(), 121) AS en;
END
ELSE IF @fase = 6
BEGIN
    -- 990007 se reconfirma después del botón: evento nuevo → sí es Modificación (version 2).
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID)
    VALUES (990007, 15, DATEADD(second, 1, GETDATE()), N'u14', NEWID());
    SELECT 'fase 6: 990007 reconfirmado tras el botón' AS paso, CONVERT(varchar(23), GETDATE(), 121) AS en;
END
ELSE IF @fase = 7
BEGIN
    -- 990008 y 990009 en estatus 10: el vendedor pulsa el botón ANTES del firme.
    INSERT INTO SYSADM.BW_AUFTR_KOPF (ID, STATUS, DATUM_ERF) VALUES
        (990008, 10, CAST(GETDATE() AS date)), (990009, 10, CAST(GETDATE() AS date));
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID) VALUES
        (990008, 10, GETDATE(), N'u14', NEWID()), (990009, 10, GETDATE(), N'u14', NEWID());
    INSERT INTO MILLET_INTEGRACION.dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
    VALUES (N'990008', 1, 1), (N'990009', 1, 1);
    SELECT 'fase 7: 990008 y 990009 avisados por el botón en 10' AS paso, CONVERT(varchar(23), GETDATE(), 121) AS en;
END
ELSE IF @fase = 8
BEGIN
    -- 990008 llega a 15 → Modificación v2 (no otra Alta). 990009 se cancela → Cancelación v2.
    UPDATE SYSADM.BW_AUFTR_KOPF SET STATUS = 15  WHERE ID = 990008;
    UPDATE SYSADM.BW_AUFTR_KOPF SET STATUS = 900 WHERE ID = 990009;
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID) VALUES
        (990008, 15, DATEADD(second, 1, GETDATE()), N'u14', NEWID()),
        (990009, 900, DATEADD(second, 1, GETDATE()), N'u14', NEWID());
    SELECT 'fase 8: 990008 a 15 y 990009 cancelado' AS paso, CONVERT(varchar(23), GETDATE(), 121) AS en;
END
