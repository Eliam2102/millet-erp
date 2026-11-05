-- DEMO sintético de PT5.10 · pedidos internos Serie 9000 (descarga del inventario del taller de Circuito). Idempotente:
-- ON CONFLICT DO NOTHING, sin borrados. Se leen de las mismas "vistas" dbo.vw_erp_pedido_* que usa Facturación, pero NO
-- tienen fila en dbo.aw_solicitud_pedido (el aviso 15/42 excluye 90000000–99999999), así que nunca llegan a la bandeja
-- de pedidos facturables. 90990010: 3 hojas 2440x3660; 90990011: 2 hojas 2140x3300 (otra medida del mismo vidrio).
-- Producto: la clave A+W del primer artículo en M2 de aw_origen.erp_articulo (pasar -v producto_ref=... para fijar otro).
BEGIN;
SELECT coalesce(nullif(:'producto_ref', ''), (SELECT producto_ref::text FROM aw_origen.erp_articulo
        WHERE NOT baja AND upper(unidad_medida) IN ('M2', 'M²') AND descripcion IS NOT NULL ORDER BY producto_ref LIMIT 1)) AS producto_ref \gset
INSERT INTO dbo.vw_erp_pedido_cabecera (numero_pedido, numero_sucursal, cliente_ref, cliente_nombre, divisa, notas_pedido,
    canal_ventas, clase, fecha_transaccion, estado_origen, total_cantidad, total_m2, importe_total)
VALUES ('90990010', 'CIRCUITO', 'TALLER', 'TALLER CIRCUITO · MOVIMIENTO INTERNO', 'MXN', 'Pedido interno Serie 9000: descarga de hojas del taller',
        NULL, NULL, current_date, '119', 3, 26.7912, 0),
       ('90990011', 'CIRCUITO', 'TALLER', 'TALLER CIRCUITO · MOVIMIENTO INTERNO', 'MXN', 'Pedido interno Serie 9000: descarga de hojas del taller',
        NULL, NULL, current_date, '119', 2, 14.124, 0)
ON CONFLICT DO NOTHING;
INSERT INTO dbo.vw_erp_pedido_linea (numero_pedido, numero_posicion, producto_ref, descripcion, cantidad, unidad_medida, importe_pieza)
VALUES ('90990010', 1, :'producto_ref', 'VIDRIO FLOAT CLARO 6MM 2440x3660', 3, 'PZA', 0),
       ('90990011', 1, :'producto_ref', 'VIDRIO FLOAT CLARO 6MM 2140x3300', 2, 'PZA', 0)
ON CONFLICT DO NOTHING;
INSERT INTO dbo.vw_erp_pedido_componente (numero_pedido, numero_posicion, producto_ref, descripcion, ancho_mm, alto_mm, m2_por_pieza, importe)
SELECT '90990010', 1, :'producto_ref', 'HOJA 2440x3660', 2440, 3660, 8.9304, 0
 WHERE NOT EXISTS (SELECT 1 FROM dbo.vw_erp_pedido_componente WHERE numero_pedido = '90990010')
UNION ALL
SELECT '90990011', 1, :'producto_ref', 'HOJA 2140x3300', 2140, 3300, 7.062, 0
 WHERE NOT EXISTS (SELECT 1 FROM dbo.vw_erp_pedido_componente WHERE numero_pedido = '90990011');
COMMIT;
