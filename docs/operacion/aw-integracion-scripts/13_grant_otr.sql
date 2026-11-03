-- Propuesta para el contrato I13.4 · no aplicar sin acuerdo
-- Ejecutar únicamente después de acordar con Millet y Jorge, en MILLET_INTEGRACION(_DEV).
-- No ejecutar en una base interna del proveedor A+W. DDL puente propiedad del ERP.
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() NOT IN (N'MILLET_INTEGRACION',N'MILLET_INTEGRACION_DEV')
    THROW 50001, 'Seleccione la BD puente MILLET_INTEGRACION(_DEV).', 1;
BEGIN TRANSACTION;

-- Usuarios contenidos ya asociados por el DBA; nombres propuestos por confirmar con Jorge.
-- El ERP no recibe UPDATE de columnas A+W ni DELETE, y A+W no recibe UPDATE de datos ERP.
GRANT SELECT ON dbo.aw_otr_envio TO [millet_erp_integracion];
GRANT SELECT ON dbo.aw_otr_envio_linea TO [millet_erp_integracion];
GRANT UPDATE (erp_resultado, erp_motivo, erp_procesada_at) ON dbo.aw_otr_envio TO [millet_erp_integracion];
GRANT SELECT ON dbo.erp_otr_estado TO [millet_erp_integracion];
GRANT INSERT ON dbo.erp_otr_estado TO [millet_erp_integracion];
GRANT SELECT ON dbo.aw_otr_envio TO [aw_otr];
GRANT SELECT ON dbo.aw_otr_envio_linea TO [aw_otr];
GRANT INSERT ON dbo.aw_otr_envio TO [aw_otr];
GRANT INSERT ON dbo.aw_otr_envio_linea TO [aw_otr];
GRANT SELECT ON dbo.erp_otr_estado TO [aw_otr];
GRANT UPDATE (aw_acuse_at, aw_resultado, aw_motivo) ON dbo.erp_otr_estado TO [aw_otr];
COMMIT;
GO
