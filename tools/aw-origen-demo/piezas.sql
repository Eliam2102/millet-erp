-- DEMO sintético del flujo PT-01 (escaneos de pieza terminada). Idempotente: ON CONFLICT DO NOTHING, sin borrados.
-- Casos: 900101 (CIRCUITO, 4 piezas pedidas): piezas 1 y 2 en la estación 2821, una reposición de la pieza 1 (misma
-- etiqueta, hora posterior), una pieza 5 que excede la cantidad, un escaneo sin terminar (fertig=0) y uno en la
-- estación 2835 (fuera de la lista de TI). 900199 no está en Facturación. 900102 (CANCUN) una pieza.
-- Las etiquetas son de 9 caracteres (SRC-114). Las horas se calculan relativas a now() (redondeadas al segundo) para que las reposiciones sean posteriores.
BEGIN;
INSERT INTO dbo.or_avbereich (auftnr, avbereich)
VALUES ('900101', 'CIRCUITO'), ('900199', 'CIRCUITO'), ('900102', 'CANCUN')
ON CONFLICT DO NOTHING;
INSERT INTO dbo.pool_pos (auftnr, pos, u_pos, ae_intpos, menge)
VALUES ('900101', 1, 0, 1, 4), ('900199', 1, 0, 1, 1), ('900102', 1, 0, 1, 2)
ON CONFLICT DO NOTHING;
-- Operación 7 = entrada a producto terminado de cada pieza (text1 = área que la registró).
INSERT INTO dbo.pool_bearbeit (auftnr, pos, u_pos, teile_nr, bearb_nr, text1)
SELECT '900101', 1, 0, n::text, 7, 'PT Conkal · Templado 6 mm claro 800 x 1200' FROM generate_series(1, 5) n
UNION ALL SELECT '900199', 1, 0, '1', 7, 'PT Conkal · Laminado 3+3 mm incoloro 600 x 900'
UNION ALL SELECT '900102', 1, 0, '1', 7, 'PT Conkal · Doble vidrio hermético 6-12-6 mm 1000 x 1500'
ON CONFLICT DO NOTHING;
INSERT INTO dbo.awbar_bearbeit (etikettnr, bearb_zeit, auftnr, pos, u_pos, teile_nr, fertig, esnr_ist, bearb_nr)
VALUES ('PT9001011', date_trunc('second', now()) - interval '10 minutes', '900101', 1, 0, '1', 1, 2821, 7),
       ('PT9001012', date_trunc('second', now()) - interval '9 minutes',  '900101', 1, 0, '2', 1, 2821, 7),
       -- reposición de la pieza 1: misma etiqueta, escaneo posterior
       ('PT9001011', date_trunc('second', now()) - interval '5 minutes',  '900101', 1, 0, '1', 1, 2821, 7),
       -- pieza 5 de un pedido de 4: excede la cantidad
       ('PT9001015', date_trunc('second', now()) - interval '8 minutes',  '900101', 1, 0, '5', 1, 2821, 7),
       -- escaneo sin terminar
       ('PT9001013', date_trunc('second', now()) - interval '7 minutes',  '900101', 1, 0, '3', 0, 2821, 7),
       -- estación que no es de PT (2835 está entre 2831 y 2840)
       ('PT9001014', date_trunc('second', now()) - interval '6 minutes',  '900101', 1, 0, '4', 1, 2835, 7),
       -- pedido que no está en Facturación
       ('PT9001991', date_trunc('second', now()) - interval '4 minutes',  '900199', 1, 0, '1', 1, 2821, 7),
       -- otra sucursal
       ('PT9001021', date_trunc('second', now()) - interval '3 minutes',  '900102', 1, 0, '1', 1, 2821, 7)
ON CONFLICT DO NOTHING;
-- Rotura de la pieza 2: etiqueta + 01 con bruch_flag = 1 (SRC-114, 29-oct); no cuenta como terminada.
INSERT INTO dbo.awbar_bearbeit (etikettnr, bearb_zeit, auftnr, pos, u_pos, teile_nr, fertig, esnr_ist, bearb_nr, bruch_flag)
VALUES ('PT900101201', date_trunc('second', now()) - interval '2 minutes', '900101', 1, 0, '2', 1, 2821, 7, 1)
ON CONFLICT DO NOTHING;
-- Albaranes de A+W (script 20): 900101 pos. 1 sale en dos albaranes parciales (1 + 1 pieza, el segundo desde un pedido
-- hijo de expedición parcial) y 900102 pos. 1 en uno. PT descuenta las piezas que estén en PT (idempotente por albarán).
INSERT INTO dbo.vw_erp_albaran_pos (numero_pedido, numero_posicion, pedido_albaran, posicion_albaran, piezas, albaran_folio, albaran_fecha, estatus_pedido)
VALUES ('900101', 1, '900101', 1, 1, 9100001, current_date, 72),
       ('900101', 1, '910101', 1, 1, 9100002, current_date, 72),
       ('900102', 1, '900102', 1, 1, 9100003, current_date, 72)
ON CONFLICT DO NOTHING;
-- A4.8 · componentes por composición: 900301 insulado con argón 530000 y separador 520042 (875.51 x 1041.65 mm),
-- 900302 laminado con PVB 419010 bajo la capa laminada 3000000, 900303 insulado sin gas ni separador (solo vidrio,
-- excepción) y una rotura del 900301 (no descuenta).
INSERT INTO dbo.or_avbereich (auftnr, avbereich)
VALUES ('900301', 'CIRCUITO'), ('900302', 'CIRCUITO'), ('900303', 'CIRCUITO')
ON CONFLICT DO NOTHING;
INSERT INTO dbo.pool_pos (auftnr, pos, u_pos, ae_intpos, menge, breite, hoehe, gas_flag, gas1_artnr)
VALUES ('900301', 1, 0, 1, 2, 875510, 1041650, 1, 530000), ('900302', 1, 0, 1, 1, 600000, 900000, 0, 0),
       ('900303', 1, 0, 1, 1, 700000, 1000000, 0, 0)
ON CONFLICT DO NOTHING;
INSERT INTO dbo.pool_teile (auftnr, pos, u_pos, teile_nr, oberteil_nr, artnr, breite, hoehe)
VALUES ('900301', 1, 0, '2000000', '0', 520042, 875510, 1041650),
       ('900302', 1, 0, '3000000', '0', 410000, 600000, 900000),
       ('900302', 1, 0, '3020000', '3000000', 419010, 600000, 900000)
ON CONFLICT DO NOTHING;
INSERT INTO dbo.awbar_bearbeit (etikettnr, bearb_zeit, auftnr, pos, u_pos, teile_nr, fertig, esnr_ist, bearb_nr, bruch_flag, art_nr)
VALUES ('031900301', date_trunc('second', now()) - interval '10 minutes', '900301', 1, 0, '0', 1, 1622, 0, 0, 999970),
       ('031900302', date_trunc('second', now()) - interval '9 minutes',  '900302', 1, 0, '3000000', 1, 1530, 0, 0, 999980),
       ('031900303', date_trunc('second', now()) - interval '8 minutes',  '900303', 1, 0, '0', 1, 1622, 0, 0, 999970),
       ('03190030101', date_trunc('second', now()) - interval '7 minutes', '900301', 1, 0, '0', 1, 1622, 0, 1, 999970)
ON CONFLICT DO NOTHING;
COMMIT;
