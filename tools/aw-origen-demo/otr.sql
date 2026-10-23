-- DEMO sintético. Pase sucursal_clave_aw y cliente_numero_aw de los catálogos DEMO.
-- No borra resultados ni salidas: repetir la carga es inocuo.
-- psql -v sucursal_clave_aw='MID' -v cliente_numero_aw='DEMO-CLI-EXP-1' -f otr.sql
BEGIN;
INSERT INTO dbo.aw_otr_envio (otr_envio_id,version,cliente_numero_aw,sucursal_clave_aw,cliente,direccion_entrega,producto,numero_contenedor,ruta,bultos,peso_kg,fecha_carga_prevista,operacion)
VALUES ('DEMO-OTR-1',1,:'cliente_numero_aw',:'sucursal_clave_aw','DEMO · Cliente extranjero 1','DEMO · Entrega ficticia','DEMO · Vidrio','DEMO-OTR-C1','DEMO-RUTA',2,100,'2026-10-20','Alta'),
       ('DEMO-OTR-1',2,:'cliente_numero_aw',:'sucursal_clave_aw','DEMO · Cliente extranjero 1','DEMO · Entrega ficticia','DEMO · Vidrio','DEMO-OTR-C1','DEMO-RUTA',2,125,'2026-10-20','Modificacion'),
       ('DEMO-OTR-2',1,:'cliente_numero_aw',:'sucursal_clave_aw','DEMO · Cliente extranjero 1','DEMO · Entrega ficticia','DEMO · Vidrio','DEMO-OTR-C2','DEMO-RUTA',1,50,'2026-10-21','Alta')
ON CONFLICT (otr_envio_id,version) DO NOTHING;
INSERT INTO dbo.aw_otr_envio_linea (otr_envio_id,version,numero_pedido,posicion,cantidad,unidad)
VALUES ('DEMO-OTR-1',1,'DEMO-PED-EXP-P1',1,10,'H87'), ('DEMO-OTR-1',2,'DEMO-PED-EXP-P1',1,10,'H87'),
       ('DEMO-OTR-2',1,'DEMO-PED-INEXISTENTE',1,5,'H87')
ON CONFLICT DO NOTHING;
COMMIT;
