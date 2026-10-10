-- Seed 100 % sintético del origen de demo. Borra y reinserta todo (sirve también para deshacer cambios a mano).
-- Referencias <= 100000, como el sandbox de A+W (las reales de productos empiezan en 100002).
-- Casos incluidos a propósito: cliente sin NAME1 (id 58) y producto sin descripción (46) = error por fila;
-- producto con unidad sin equivalencia (47) = pendiente; producto en baja (45); condición de pago sin coincidencia.
TRUNCATE aw_origen.ku_kunden, aw_origen.ka_zahlbed, aw_origen.erp_articulo;

INSERT INTO aw_origen.ka_zahlbed (bez, bruttotage, nummer) VALUES
 ('<indf>', NULL, 0), ('CONTADO', 0, 1), ('REPARTO', 0, 2), ('7 DIAS', 7, 3), ('15 DIAS', 15, 4), ('21 DIAS', 21, 5),
 ('30 DIAS', 30, 6), ('45 DIAS', 45, 7), ('60 DIAS', 60, 8), ('75 DIAS', 75, 9), ('90 DIAS', 90, 10);

-- ───────── Clientes: 60 ─────────
INSERT INTO aw_origen.ku_kunden (id, mandant, name1, name2, name3, strasse, ort, plz, provinz, land, ust_id, steuernummer,
                                 tlf1, tlf2, mail, zahlbed, waehrung, kredit_limit, kredit_limit1, kredit_limit_net,
                                 kz_status, kz_gesperrt, datum, transaction_time)
SELECT i, 1,
  CASE WHEN i = 58 THEN '' ELSE 'CLIENTE DEMO ' || lpad(i::text, 3, '0') END,
  CASE WHEN i % 20 = 0 THEN 'SA DE CV DEMO' ELSE '' END,
  CASE WHEN i % 15 = 0 THEN 'CONTACTO DEMO' ELSE '' END,
  CASE WHEN i % 4 = 0 THEN 'CALLE DEMO ' || i ELSE '' END,
  (ARRAY['CIUDAD DE MEXICO','MONTERREY','GUADALAJARA','CHIHUAHUA','CANCUN','MERIDA'])[i % 6 + 1],
  CASE WHEN i % 5 = 0 THEN '' ELSE lpad(i::text, 5, '0') END,
  (ARRAY['CDMX','NL','JAL','CHIH','QROO','YUC'])[i % 6 + 1],
  'MEX',
  CASE WHEN i % 12 = 0 THEN ''                              -- sin identificación fiscal
       WHEN i % 3 = 0 THEN 'XAXX010101000'                  -- RFC genérico repetido
       WHEN i % 11 = 0 THEN 'XEXX010101000'
       ELSE 'DEMO' || lpad(i::text, 3, '0') END,
  '',
  CASE WHEN i % 3 = 0 THEN '' ELSE '55' || lpad(i::text, 8, '0') END,
  '',
  CASE WHEN i % 3 = 0 THEN '' ELSE 'cliente' || lpad(i::text, 3, '0') || '@demo.invalid' END,
  CASE WHEN i % 53 = 0 THEN 'contado'                       -- minúscula: no coincide con el catálogo
       WHEN i % 47 = 0 THEN 'CREDITO DEMO'                  -- sin coincidencia en el catálogo
       WHEN i % 13 = 0 THEN 'REPARTO' WHEN i % 10 = 1 THEN '<indf>' WHEN i % 17 = 0 THEN '45 DIAS'
       WHEN i % 19 = 0 THEN '30 DIAS' WHEN i % 23 = 0 THEN '60 DIAS' WHEN i = 29 THEN '90 DIAS' ELSE 'CONTADO' END,
  CASE WHEN i = 44 THEN 'Euro' WHEN i % 10 = 7 THEN 'USD' ELSE 'PESOSMX' END,
  (i % 5) * 25000, (i % 5) * 25000, (i % 5) * 25000.0,
  CASE WHEN i % 8 < 3 THEN 1 ELSE 2 END,
  CASE WHEN i % 10 = 0 THEN 1 ELSE 0 END,
  DATE '2020-01-01' + i,
  CASE WHEN i % 20 = 0 THEN TIMESTAMP '2026-01-01 10:00:00' END
FROM generate_series(1, 60) AS i;

-- ───────── Productos: float (1-12), componentes PVB (41-42) y perfiles intercalarios (43-44) ─────────
INSERT INTO aw_origen.erp_articulo (producto_ref, descripcion, unidad_medida, codigo_modelo, grupo, tipo, wgr, wgr_descripcion, variantes)
SELECT i, 'VIDRIO FLOAT ' || c.color || ' ' || c.esp || 'MM', 'm²', 'FLOAT ' || c.color, 'Float', 'Vidrio plano', '310', 'VIDRIO FLOAT',
  jsonb_build_array(
    jsonb_build_object('claveVariante', '2440x3660', 'altoMm', 3660, 'anchoMm', 2440, 'espesorMm', c.esp, 'composicion', NULL),
    jsonb_build_object('claveVariante', '1830x2440', 'altoMm', 2440, 'anchoMm', 1830, 'espesorMm', c.esp, 'composicion', NULL))
FROM generate_series(1, 12) AS i
CROSS JOIN LATERAL (SELECT (ARRAY['CLARO','BRONCE','GRIS'])[(i - 1) / 4 + 1] AS color, (ARRAY[3,6,8,10])[(i - 1) % 4 + 1] AS esp) AS c;

INSERT INTO aw_origen.erp_articulo (producto_ref, descripcion, unidad_medida, codigo_modelo, grupo, tipo, wgr, wgr_descripcion, variantes) VALUES
 (41, 'PVB 0.76MM',  'm²',    'PVB 0.76', 'Interlayer', 'Relleno', '320', 'INTERLAYER', '[{"claveVariante":"BASE","altoMm":null,"anchoMm":null,"espesorMm":0.76,"composicion":null}]'),
 (42, 'PVB 1.52MM',  'm²',    'PVB 1.52', 'Interlayer', 'Relleno', '320', 'INTERLAYER', '[{"claveVariante":"BASE","altoMm":null,"anchoMm":null,"espesorMm":1.52,"composicion":null}]'),
 (43, 'PERFIL INTERCALARIO 9MM',  'm lin.', 'PERFIL 9',  'Intercalario', 'Perfil intercalario', '330', 'INTERCALARIOS', '[{"claveVariante":"BASE","altoMm":null,"anchoMm":null,"espesorMm":9,"composicion":null}]'),
 (44, 'PERFIL INTERCALARIO 12MM', 'm lin.', 'PERFIL 12', 'Intercalario', 'Perfil intercalario', '330', 'INTERCALARIOS', '[{"claveVariante":"BASE","altoMm":null,"anchoMm":null,"espesorMm":12,"composicion":null}]');

-- ───────── Templado (13-24): uno por cada float ─────────
INSERT INTO aw_origen.erp_articulo (producto_ref, descripcion, unidad_medida, codigo_modelo, grupo, tipo, wgr, wgr_descripcion, variantes, componentes)
SELECT f.producto_ref + 12, replace(f.descripcion, 'FLOAT', 'TEMPLADO'), 'm²', replace(f.codigo_modelo, 'FLOAT', 'TEMPLADO'),
  'Templado', 'VTE', '370', 'VIDRIO TEMPLADO',
  jsonb_build_array(jsonb_build_object('claveVariante', 'BASE', 'altoMm', NULL, 'anchoMm', NULL, 'espesorMm', e.esp, 'composicion', NULL)),
  jsonb_build_array(jsonb_build_object('orden', 1, 'nivel', 1, 'padreOrden', NULL, 'ref', f.producto_ref::text,
                                       'descripcion', f.descripcion, 'tipo', f.tipo, 'espesorMm', e.esp))
FROM aw_origen.erp_articulo AS f
CROSS JOIN LATERAL (SELECT (f.variantes -> 0 ->> 'espesorMm')::numeric AS esp) AS e
WHERE f.producto_ref BETWEEN 1 AND 12;

-- ───────── Laminado (25-32): float claro (1-4) × PVB 0.76 / 1.52 ─────────
INSERT INTO aw_origen.erp_articulo (producto_ref, descripcion, unidad_medida, codigo_modelo, grupo, tipo, wgr, wgr_descripcion, variantes, componentes)
SELECT 24 + f.producto_ref + CASE p.ref WHEN 41 THEN 0 ELSE 4 END,
  'VIDRIO LAMINADO ' || e.esp || '+' || e.esp || ' PVB ' || p.pvb || 'MM CLARO', 'm²',
  'LAMINADO ' || e.esp || '+' || e.esp, 'Laminado', 'VLA', '380', 'VIDRIO LAMINADO',
  jsonb_build_array(
    jsonb_build_object('claveVariante', '2440x3660', 'altoMm', 3660, 'anchoMm', 2440, 'espesorMm', 2 * e.esp + p.pvb, 'composicion', c.txt),
    jsonb_build_object('claveVariante', '1830x2440', 'altoMm', 2440, 'anchoMm', 1830, 'espesorMm', 2 * e.esp + p.pvb, 'composicion', c.txt)),
  jsonb_build_array(
    jsonb_build_object('orden', 1, 'nivel', 1, 'padreOrden', NULL, 'ref', f.producto_ref::text, 'descripcion', f.descripcion, 'tipo', f.tipo, 'espesorMm', e.esp),
    jsonb_build_object('orden', 2, 'nivel', 1, 'padreOrden', NULL, 'ref', p.ref::text, 'descripcion', 'PVB ' || p.pvb || 'MM', 'tipo', 'Relleno', 'espesorMm', p.pvb),
    jsonb_build_object('orden', 3, 'nivel', 1, 'padreOrden', NULL, 'ref', f.producto_ref::text, 'descripcion', f.descripcion, 'tipo', f.tipo, 'espesorMm', e.esp))
FROM aw_origen.erp_articulo AS f
CROSS JOIN (VALUES (0.76, 41), (1.52, 42)) AS p(pvb, ref)
CROSS JOIN LATERAL (SELECT (f.variantes -> 0 ->> 'espesorMm')::numeric AS esp) AS e
CROSS JOIN LATERAL (SELECT e.esp || '+' || p.pvb || '+' || e.esp AS txt) AS c
WHERE f.producto_ref BETWEEN 1 AND 4;

-- ───────── Doble vidrio hermético (33-40): float claro/gris de 3 y 6 mm × cámara 9 / 12 mm ─────────
INSERT INTO aw_origen.erp_articulo (producto_ref, descripcion, unidad_medida, codigo_modelo, grupo, tipo, wgr, wgr_descripcion, variantes, componentes)
SELECT 32 + row_number() OVER (ORDER BY f.producto_ref, k.cam),
  'DOBLE VIDRIO HERMETICO ' || e.esp || '+' || k.cam || '+' || e.esp || ' ' || split_part(f.codigo_modelo, ' ', 2), 'm²',
  'DVH ' || e.esp || '+' || k.cam || '+' || e.esp, 'DVH', 'Producto', '390', 'DOBLE VIDRIO HERMETICO',
  jsonb_build_array(
    jsonb_build_object('claveVariante', '2440x3660', 'altoMm', 3660, 'anchoMm', 2440, 'espesorMm', 2 * e.esp + k.cam, 'composicion', e.esp || '+' || k.cam || '+' || e.esp),
    jsonb_build_object('claveVariante', '1830x2440', 'altoMm', 2440, 'anchoMm', 1830, 'espesorMm', 2 * e.esp + k.cam, 'composicion', e.esp || '+' || k.cam || '+' || e.esp)),
  jsonb_build_array(
    jsonb_build_object('orden', 1, 'nivel', 1, 'padreOrden', NULL, 'ref', f.producto_ref::text, 'descripcion', f.descripcion, 'tipo', f.tipo, 'espesorMm', e.esp),
    jsonb_build_object('orden', 2, 'nivel', 1, 'padreOrden', NULL, 'ref', k.ref::text, 'descripcion', 'PERFIL INTERCALARIO ' || k.cam || 'MM', 'tipo', 'Perfil intercalario', 'espesorMm', k.cam),
    jsonb_build_object('orden', 3, 'nivel', 1, 'padreOrden', NULL, 'ref', f.producto_ref::text, 'descripcion', f.descripcion, 'tipo', f.tipo, 'espesorMm', e.esp))
FROM aw_origen.erp_articulo AS f
CROSS JOIN (VALUES (9, 43), (12, 44)) AS k(cam, ref)
CROSS JOIN LATERAL (SELECT (f.variantes -> 0 ->> 'espesorMm')::numeric AS esp) AS e
WHERE f.producto_ref IN (1, 2, 9, 10);

-- ───────── Casos límite ─────────
INSERT INTO aw_origen.erp_articulo (producto_ref, descripcion, unidad_medida, baja, transaction_time, codigo_modelo, grupo, tipo, wgr, wgr_descripcion, variantes) VALUES
 (45, 'VIDRIO FLOAT CLARO 4MM (DESCONTINUADO)', 'm²', true,  TIMESTAMP '2026-01-01 10:00:00', 'FLOAT CLARO', 'Float', 'Vidrio plano', '310', 'VIDRIO FLOAT', '[{"claveVariante":"BASE","altoMm":null,"anchoMm":null,"espesorMm":4,"composicion":null}]'),
 (46, '',                                       'm²', false, NULL, 'SIN DESCRIPCION', 'Float', 'Vidrio plano', '310', 'VIDRIO FLOAT', '[]'),
 (47, 'ROLLO DE PELICULA DEMO',                 'Rollo', false, NULL, 'PELICULA', 'Accesorios', 'Producto', NULL, NULL, '[]');
