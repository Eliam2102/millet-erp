-- A4.8 / F1-MP-07 · Normas de consumo FICTICIAS para el ambiente de pruebas.
-- Deja listos argón, separador y PVB para que el descuento por composición de cada pieza terminada en A+W se pueda
-- probar de punta a punta. NO son datos de Millet: las normas reales (consumo, merma y factor del argón) las entrega
-- Producción y se capturan en la tarea 86e3yyy8u. Cada norma queda marcada en created_by y cada artículo en su nombre
-- con "NORMA FICTICIA — pendiente de Millet"; Almacén las edita en el catálogo de normas igual que las reales.
--
-- Uso (solo ambiente de pruebas, nunca dev en línea ni main), con la API ya migrada y arrancada una vez:
--   psql "$CONN" -v ON_ERROR_STOP=1 -f backend/scripts/almacen-normas-consumo-ficticias.sql
-- y para encender el descuento en la API de pruebas:
--   IntegracionesAw__PiezasPt__ConsumoComponentesHabilitado=true  IntegracionesAw__PiezasPt__EmpresaId=<empresa>
--   (con origen A+W Demo; con origen Real además exige ContratoI134Firmado y la conexión de A+W)
-- Idempotente: no pisa lo que Almacén ya haya editado ni la existencia ya movida.
BEGIN;

-- La sucursal del pedido en A+W (OR_AVBEREICH) debe estar ligada a una sucursal del ERP; sin eso el consumo queda en revisión.
UPDATE compartido.sucursales SET clave_aw = 'CIRCUITO'
WHERE clave = 'MID' AND clave_aw IS NULL AND NOT EXISTS (SELECT 1 FROM compartido.sucursales WHERE clave_aw = 'CIRCUITO');

-- (referencia A+W, clave ERP, nombre, unidad, consumo por unidad, merma %, factor m²→m³, existencia, costo MXN por unidad)
CREATE TEMP TABLE norma_ficticia ON COMMIT DROP AS
SELECT * FROM (VALUES
  ('530000', 'FIC-ARGON',    'Gas argón (NORMA FICTICIA — pendiente de Millet)',           'M3', 1.000000::numeric, 10.0000::numeric, 0.012000::numeric,   5.0000::numeric, 1500.00::numeric),
  ('520042', 'FIC-SEPARADOR','Separador de aluminio 12 mm (NORMA FICTICIA — pendiente de Millet)', 'M',  1.000000, 3.0000, NULL,  800.0000, 38.50),
  ('419010', 'FIC-PVB',      'Interlayer PVB 0.76 mm (NORMA FICTICIA — pendiente de Millet)', 'M2', 1.000000, 5.0000, NULL,  300.0000, 96.00)
) AS t(ref_aw, clave, nombre, unidad, consumo, merma, factor, existencia, costo);

INSERT INTO compartido.producto_aw (id, referencia_externa, descripcion, unidad_medida, origen, estatus, version, created_at, updated_at, created_by, updated_by)
SELECT gen_random_uuid(), ref_aw, nombre, unidad, 0, 0, 1, now(), now(), 'NORMA FICTICIA — pendiente de Millet', 'NORMA FICTICIA — pendiente de Millet'
FROM norma_ficticia
ON CONFLICT (referencia_externa) DO NOTHING;

INSERT INTO compartido.articulos (id, clave, nombre, unidad_medida_default, naturaleza, estatus, version, created_at, updated_at, created_by, updated_by, producto_aw_id)
SELECT gen_random_uuid(), n.clave, n.nombre, n.unidad, 0, 0, 1, now(), now(), 'NORMA FICTICIA — pendiente de Millet', 'NORMA FICTICIA — pendiente de Millet', p.id
FROM norma_ficticia n JOIN compartido.producto_aw p ON p.referencia_externa = n.ref_aw
WHERE NOT EXISTS (SELECT 1 FROM compartido.articulos a WHERE a.producto_aw_id = p.id)
ON CONFLICT DO NOTHING;

INSERT INTO almacen.normas_consumo_componente (id, empresa_id, articulo_id, consumo_por_unidad, porcentaje_merma, factor_m3por_m2, version, created_at, updated_at, created_by, updated_by)
SELECT gen_random_uuid(), e.id, a.id, n.consumo, n.merma, n.factor, 1, now(), now(), 'NORMA FICTICIA — pendiente de Millet', 'NORMA FICTICIA — pendiente de Millet'
FROM norma_ficticia n
JOIN compartido.producto_aw p ON p.referencia_externa = n.ref_aw
JOIN compartido.articulos a ON a.producto_aw_id = p.id
CROSS JOIN (SELECT id FROM compartido.empresas WHERE rfc = 'MID010101AAA') e
ON CONFLICT (empresa_id, articulo_id) DO NOTHING;

-- Existencia ficticia en materiales directos del almacén de materia prima de MID (el descuento sale de la sucursal del pedido).
INSERT INTO almacen.saldos_inventario (sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at, ubicacion_id)
SELECT s.id, a.id, n.existencia, n.costo, now(), u.id
FROM norma_ficticia n
JOIN compartido.producto_aw p ON p.referencia_externa = n.ref_aw
JOIN compartido.articulos a ON a.producto_aw_id = p.id
CROSS JOIN almacen.almacenes al
JOIN almacen.sub_almacenes s ON s.almacen_id = al.id AND s.clave = 'MAT-DIR'
JOIN almacen.ubicaciones u ON u.sub_almacen_id = s.id AND u.es_default
WHERE al.clave = 'ALM-MID-MP'
  AND NOT EXISTS (SELECT 1 FROM almacen.saldos_inventario x WHERE x.articulo_id = a.id)
ON CONFLICT DO NOTHING;

COMMIT;
