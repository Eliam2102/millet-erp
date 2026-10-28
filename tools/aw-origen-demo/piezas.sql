-- DEMO sintético del flujo PT-01 (escaneos de pieza terminada). Idempotente: ON CONFLICT DO NOTHING, sin borrados.
-- Casos: 900101 (CIRCUITO, 4 piezas pedidas): piezas 1 y 2 en la estación 2821, una reposición de la pieza 1 (misma
-- etiqueta, hora posterior), una pieza 5 que excede la cantidad, un escaneo sin terminar (fertig=0) y uno en la
-- estación 2900 (fuera de la lista configurada). 900199 no está en Facturación. 900102 (CANCUN) una pieza.
-- Las horas se calculan relativas a now() (redondeadas al segundo) para que las reposiciones sean posteriores.
BEGIN;
INSERT INTO dbo.or_avbereich (auftnr, avbereich)
VALUES ('900101', 'CIRCUITO'), ('900199', 'CIRCUITO'), ('900102', 'CANCUN')
ON CONFLICT DO NOTHING;
INSERT INTO dbo.pool_pos (auftnr, pos, u_pos, ae_intpos, menge)
VALUES ('900101', 1, 0, 1, 4), ('900199', 1, 0, 1, 1), ('900102', 1, 0, 1, 2)
ON CONFLICT DO NOTHING;
INSERT INTO dbo.pool_bearbeit (auftnr, pos, u_pos, esnr, text1)
VALUES ('900101', 1, 0, 2821, 'Templado 6 mm claro 800 x 1200'),
       ('900101', 1, 0, 2900, 'Templado 6 mm claro 800 x 1200'),
       ('900199', 1, 0, 2821, 'Laminado 3+3 mm incoloro 600 x 900'),
       ('900102', 1, 0, 2821, 'Doble vidrio hermético 6-12-6 mm 1000 x 1500')
ON CONFLICT DO NOTHING;
INSERT INTO dbo.awbar_bearbeit (etikettnr, bearb_zeit, auftnr, pos, u_pos, teile_nr, fertig, esnr_ist)
VALUES ('DEMO-PT-900101-1-1', date_trunc('second', now()) - interval '10 minutes', '900101', 1, 0, '1', 1, 2821),
       ('DEMO-PT-900101-1-2', date_trunc('second', now()) - interval '9 minutes',  '900101', 1, 0, '2', 1, 2821),
       -- reposición de la pieza 1: misma etiqueta, escaneo posterior
       ('DEMO-PT-900101-1-1', date_trunc('second', now()) - interval '5 minutes',  '900101', 1, 0, '1', 1, 2821),
       -- pieza 5 de un pedido de 4: excede la cantidad
       ('DEMO-PT-900101-1-5', date_trunc('second', now()) - interval '8 minutes',  '900101', 1, 0, '5', 1, 2821),
       -- escaneo sin terminar
       ('DEMO-PT-900101-1-3', date_trunc('second', now()) - interval '7 minutes',  '900101', 1, 0, '3', 0, 2821),
       -- estación fuera de la lista configurada
       ('DEMO-PT-900101-1-4', date_trunc('second', now()) - interval '6 minutes',  '900101', 1, 0, '4', 1, 2900),
       -- pedido que no está en Facturación
       ('DEMO-PT-900199-1-1', date_trunc('second', now()) - interval '4 minutes',  '900199', 1, 0, '1', 1, 2821),
       -- otra sucursal
       ('DEMO-PT-900102-1-1', date_trunc('second', now()) - interval '3 minutes',  '900102', 1, 0, '1', 1, 2821)
ON CONFLICT DO NOTHING;
COMMIT;
