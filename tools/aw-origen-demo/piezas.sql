-- DEMO LOCAL: ejecutar después de schema.sql y pedidos.sql. Pedido sintético existente 900101, partida 1 (4 piezas).
-- Configurar estación sintética 9069 -> CIRCUITO; no representa una estación real de Millet.
INSERT INTO aw_origen.awbar_bearbeit (etikettnr, auftnr, pos, teile_nr, bearb_zeit, esnr_ist, fertig)
VALUES ('DEMO-PT-900101-1-1', '900101', 1, 1, '2026-10-30T12:00:00', 9069, 1),
       ('DEMO-PT-900101-1-2', '900101', 1, 2, '2026-10-30T12:00:00', 9069, 1),
       ('DEMO-PT-900101-1-1-REPEAT', '900101', 1, 1, '2026-10-30T12:01:00', 9069, 1),
       ('DEMO-PT-900199', '900199', 1, 1, '2026-10-30T12:02:00', 9069, 1),
       ('DEMO-PT-EXCESS', '900101', 1, 5, '2026-10-30T12:03:00', 9069, 1)
ON CONFLICT DO NOTHING;
