-- U1.4 · escenarios del job de aviso (07_job_aviso_pedidos.sql) sobre el sandbox A+W.
-- Lo ejecuta u14-job-aviso.sh por fases ($(FASE) = 1, 2 o 3). Pedidos de prueba 990001–990006.
-- Requiere los defaults temporales del seed (03-defaults-temporales.sql) para insertar en KOPF/HIST.
SET NOCOUNT ON;
USE AW_SANDBOX;
DECLARE @fase int = $(FASE);
DECLARE @desde datetime = (SELECT desde FROM MILLET_INTEGRACION.dbo.aw_aviso_job_config WHERE id = 1);

IF @fase = 1
BEGIN
    -- Limpieza de corridas anteriores (solo pedidos de prueba).
    DELETE FROM SYSADM.BW_AUFTR_HIST WHERE ID BETWEEN 990001 AND 990006;
    DELETE FROM SYSADM.BW_AUFTR_KOPF WHERE ID BETWEEN 990001 AND 990006;

    -- 990001 hojas pasa a 15 (CA13.1) · 990002 máquina pasa a 42 (CA13.2)
    -- 990003 pasa a 15 (se modificará y cancelará después)
    -- 990004 cancelado sin aviso previo → se ignora
    -- 990005 en 15 desde ANTES de activar el job → no se avisa (arranque)
    -- 990006 en 15 sin evento en el historial → no se avisa
    INSERT INTO SYSADM.BW_AUFTR_KOPF (ID, STATUS, DATUM_ERF) VALUES
        (990001, 15, CAST(GETDATE() AS date)), (990002, 42, CAST(GETDATE() AS date)),
        (990003, 15, CAST(GETDATE() AS date)), (990004, 900, CAST(GETDATE() AS date)),
        (990005, 15, DATEADD(day, -3, CAST(GETDATE() AS date))), (990006, 15, CAST(GETDATE() AS date));
    INSERT INTO SYSADM.BW_AUFTR_HIST (ID, STATUS_ID, DATUM, BEARBEITER, ROWID) VALUES
        (990001, 15, GETDATE(), N'u14', NEWID()), (990002, 42, GETDATE(), N'u14', NEWID()),
        (990003, 15, GETDATE(), N'u14', NEWID()), (990004, 900, GETDATE(), N'u14', NEWID()),
        (990005, 15, DATEADD(day, -3, @desde), N'u14', NEWID());
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
