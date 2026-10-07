-- Seed 100% sintético, idempotente (borra y reinserta todo; sirve también para deshacer mutaciones).
SET NOCOUNT ON;
DELETE FROM SYSADM.BA_STUKL;
DELETE FROM SYSADM.BA_PRODUKTE_BEZ;
DELETE FROM SYSADM.BA_PRODUKTE;
DELETE FROM SYSADM.KU_KUNDEN;
DELETE FROM SYSADM.KA_ZAHLBED;
-- 11 filas como el real; '<indf>' = registro nulo (NUMMER 0, BRUTTOTAGE NULL). BEZ es PK (CS_AS): ni duplicados ni >1 coincidencias posibles.
INSERT SYSADM.KA_ZAHLBED (BEZ, BRUTTOTAGE, NUMMER) VALUES
 (N'<indf>', NULL, 0), (N'CONTADO', 0, 1), (N'REPARTO', 0, 2), (N'7 DIAS', 7, 3), (N'15 DIAS', 15, 4), (N'21 DIAS', 21, 5),
 (N'30 DIAS', 30, 6), (N'45 DIAS', 45, 7), (N'60 DIAS', 60, 8), (N'75 DIAS', 75, 9), (N'90 DIAS', 90, 10);
GO
