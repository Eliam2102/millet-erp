-- DEMO sintético M-445: la ruta del envío DEMO-OTR-1 pasa a 890 (cancelado) y OTR crea un envío nuevo que lo reemplaza.
-- Correr después de vincular DEMO-OTR-1 a un expediente. Repetir la carga es inocuo.
-- psql -v sucursal_clave_aw='MID' -v cliente_numero_aw='DEMO-CLI-EXP-1' -f otr-cancelacion.sql
BEGIN;
INSERT INTO dbo.aw_otr_envio (otr_envio_id,version,cliente_numero_aw,sucursal_clave_aw,cliente,direccion_entrega,producto,numero_contenedor,ruta,bultos,peso_kg,fecha_carga_prevista,operacion)
VALUES ('DEMO-OTR-1',3,:'cliente_numero_aw',:'sucursal_clave_aw','DEMO · Cliente extranjero 1','DEMO · Entrega ficticia','DEMO · Vidrio','DEMO-OTR-C1','DEMO-RUTA',2,125,'2026-10-20','Cancelacion'),
       ('DEMO-OTR-3',1,:'cliente_numero_aw',:'sucursal_clave_aw','DEMO · Cliente extranjero 1','DEMO · Entrega ficticia','DEMO · Vidrio','DEMO-OTR-C3','DEMO-RUTA-2',2,125,'2026-10-22','Alta')
ON CONFLICT (otr_envio_id,version) DO NOTHING;
INSERT INTO dbo.aw_otr_envio_linea (otr_envio_id,version,numero_pedido,posicion,cantidad,unidad)
VALUES ('DEMO-OTR-3',1,'DEMO-PED-EXP-P1',1,10,'H87')
ON CONFLICT DO NOTHING;
COMMIT;
