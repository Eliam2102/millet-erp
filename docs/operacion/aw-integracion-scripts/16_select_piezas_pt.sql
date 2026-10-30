-- REFERENCIA PT-01 / Rev. 3, 30-oct-2026. NO EJECUTADO.
-- SELECT exclusivamente; sin tablas nuevas, triggers ni acuses en A+W.
-- Parámetros del lector: @batch, @time, @barcode, @station y @station0..N.
-- Lista de estaciones de Jorge pendiente (PLATFORM-TODO PiezasPtEstacionesJorge).
-- Login y permisos: 15_create_login_lector_produccion.sql.
-- El cursor compuesto evita omitir filas con bearb_zeit idéntico dentro de varios lotes.
SELECT TOP (@batch) etikettnr, auftnr, pos, teile_nr, bearb_zeit, esnr_ist
FROM alcimdb.dbo.awbar_bearbeit WITH (NOLOCK)
WHERE fertig = 1
  AND esnr_ist IN (@station0)
  AND (bearb_zeit > @time
       OR (bearb_zeit = @time AND
           (CAST(etikettnr AS varchar(100)) COLLATE Latin1_General_100_BIN2 > @barcode
            OR (CAST(etikettnr AS varchar(100)) COLLATE Latin1_General_100_BIN2 = @barcode AND esnr_ist > @station))))
ORDER BY bearb_zeit, CAST(etikettnr AS varchar(100)) COLLATE Latin1_General_100_BIN2, esnr_ist;
-- Resultado y reintentos: producto_terminado.escaneos; cursor: reader_watermarks en ERP, por empresa y canal.
-- NOLOCK no garantiza lectura de filas confirmadas; validar orden de escritura, zona horaria y retención con TI al firmar I13.4.
