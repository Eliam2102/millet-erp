-- Pedidos en firme de DEMO para la ingesta a Facturación (flujo 2, doc integration/04). Borra y reinserta la cola y las
-- "vistas" dbo.vw_erp_pedido_*. Clientes y productos se toman de aw_origen.* (seed sintético o importado de AW_DEMO),
-- así siempre existen en lo sincronizado. Importes BRUTOS (con IVA), como los entrega A+W.
-- Para que se ingesten hace falta, en el ERP: la clave A+W de la sucursal (CIRCUITO / CANCUN) y del canal
-- (Ventas Circuito / Ventas Cancun ya vienen sembrados) y Facturacion:Workers:AwSolicitudes:EmpresaId.
-- Casos: 900101 contado con 2 líneas; 900102 a crédito (ConAnticipo) en otra sucursal; 900103 cliente que no existe
-- en A+W (cae a la bandeja de excepciones con ClienteNoExiste).
-- Re-sembrar solo contra una BD del ERP nueva: el ERP recuerda (ingesta_control) los pedidos ya procesados.
TRUNCATE dbo.aw_solicitud_pedido, dbo.vw_erp_pedido_cabecera, dbo.vw_erp_pedido_linea, dbo.vw_erp_pedido_componente;

DROP TABLE IF EXISTS demo_ref;
CREATE TEMP TABLE demo_ref AS
SELECT (SELECT id::text FROM aw_origen.ku_kunden
         WHERE coalesce(btrim(name1), '') NOT IN ('', '<indf>')
           AND coalesce(btrim(ust_id), '') NOT IN ('', 'XAXX010101000', 'XEXX010101000')
           AND land IN ('MX', 'MEX')
         ORDER BY id LIMIT 1)                                          AS cliente_a,
       (SELECT id::text FROM aw_origen.ku_kunden
         WHERE coalesce(btrim(name1), '') NOT IN ('', '<indf>')
           AND coalesce(btrim(ust_id), '') NOT IN ('', 'XAXX010101000', 'XEXX010101000')
           AND land IN ('MX', 'MEX')
         ORDER BY id OFFSET 1 LIMIT 1)                                 AS cliente_b,
       (SELECT producto_ref::text FROM aw_origen.erp_articulo
         WHERE NOT baja AND upper(unidad_medida) IN ('M2', 'M²') AND descripcion IS NOT NULL
           AND coalesce(tipo, '') NOT IN ('Proceso', 'Relleno', 'Perfil intercalario')
         ORDER BY tipo = 'VTE' DESC, producto_ref LIMIT 1)             AS producto_m2,
       (SELECT producto_ref::text FROM aw_origen.erp_articulo
         WHERE NOT baja AND upper(unidad_medida) IN ('M2', 'M²') AND descripcion IS NOT NULL
           AND coalesce(tipo, '') NOT IN ('Proceso', 'Relleno', 'Perfil intercalario')
         ORDER BY tipo = 'VLA' DESC, producto_ref DESC LIMIT 1)        AS producto_m2_b,
       (SELECT producto_ref::text FROM aw_origen.erp_articulo
         WHERE NOT baja AND upper(unidad_medida) NOT IN ('M2', 'M²') AND coalesce(btrim(descripcion), '') <> ''
           AND coalesce(tipo, '') NOT IN ('Proceso', 'Relleno', 'Perfil intercalario')
         ORDER BY upper(unidad_medida) = 'PZA' DESC, producto_ref LIMIT 1) AS producto_pza;  -- el seed sintético no trae PZA

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM demo_ref WHERE cliente_a IS NULL OR cliente_b IS NULL OR producto_m2 IS NULL
                                         OR producto_m2_b IS NULL OR producto_pza IS NULL) THEN
        RAISE EXCEPTION 'aw_origen no tiene 2 clientes con RFC y productos M2 + otra unidad: cargar seed.sql o importar primero';
    END IF;
END $$;

INSERT INTO dbo.vw_erp_pedido_cabecera (numero_pedido, numero_sucursal, cliente_ref, cliente_nombre, rfc_cliente,
    uso_cfdi, metodo_pago, forma_pago, condicion_pago, divisa, notas_pedido, canal_ventas, clase, fecha_transaccion,
    total_cantidad, total_m2, iva_porcentaje)
SELECT '900101', 'CIRCUITO', c.cliente_ref, c.razon_social, c.rfc, 'G03', 'PUE', '03', 'CONTADO', 'MXN',
       'Pedido de demo: contado, dos líneas', 'Ventas Circuito', 'MostradorInmediato', current_date, 14, 4, 16
  FROM demo_ref r JOIN dbo.vw_erp_cliente c ON c.cliente_ref = r.cliente_a
UNION ALL
SELECT '900102', 'CANCUN', c.cliente_ref, c.razon_social, c.rfc, 'G03', 'PPD', '99', '30 DIAS', 'MXN',
       'Pedido de demo: crédito a 30 días', 'Ventas Cancun', 'ConAnticipo', current_date, 6, 6, 16
  FROM demo_ref r JOIN dbo.vw_erp_cliente c ON c.cliente_ref = r.cliente_b
UNION ALL
SELECT '900103', 'CIRCUITO', '999999', 'CLIENTE QUE NO EXISTE EN A+W', NULL, 'G03', 'PUE', '01', 'CONTADO', 'MXN',
       'Pedido de demo: cliente inexistente → bandeja de excepciones', 'Ventas Circuito', 'MostradorInmediato',
       current_date, 2, 2, 16;

INSERT INTO dbo.vw_erp_pedido_linea (numero_pedido, numero_posicion, producto_ref, descripcion, cantidad, unidad_medida,
    importe_pieza, descuento_porcentaje, descuento)
SELECT '900101', 1, a.producto_ref, a.descripcion, 4, 'M2', 1160.00, 0, 0
  FROM demo_ref r JOIN dbo.vw_erp_articulo a ON a.producto_ref = r.producto_m2
UNION ALL
SELECT '900101', 2, a.producto_ref, a.descripcion, 10, 'PZA', 11.60, 0, 0
  FROM demo_ref r JOIN dbo.vw_erp_articulo a ON a.producto_ref = r.producto_pza
UNION ALL
SELECT '900102', 1, a.producto_ref, a.descripcion, 6, 'M2', 2320.00, 10, 1392.00
  FROM demo_ref r JOIN dbo.vw_erp_articulo a ON a.producto_ref = r.producto_m2_b
UNION ALL
SELECT '900103', 1, a.producto_ref, a.descripcion, 2, 'M2', 1160.00, 0, 0
  FROM demo_ref r JOIN dbo.vw_erp_articulo a ON a.producto_ref = r.producto_m2;

INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, version, creada_at)
VALUES ('900101', 1, 1, now() - interval '3 minutes'),
       ('900102', 1, 1, now() - interval '2 minutes'),
       ('900103', 1, 1, now() - interval '1 minute');
