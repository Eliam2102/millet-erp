-- U1.2 · diagnóstico previo a SeriesFiscalesContinuidad; solo lectura.
-- Si hay duplicados, Fiscal decide cuál conservar activa antes de migrar.
-- No borrar historia, secuencias ni folios. No sumar/reiniciar contadores.
SELECT empresa_id, sucursal_id, tipo_documento, count(*) AS series_activas,
       array_agg(id ORDER BY id) AS ids
FROM compartido.series
WHERE activa AND tipo_documento IN (2, 3, 5, 6)
GROUP BY empresa_id, sucursal_id, tipo_documento
HAVING count(*) > 1;

-- Series heredadas con reinicio: requieren reemplazo y folio inicial
-- conciliado por Fiscal; MAX por sí solo no confirma continuidad productiva.
SELECT s.id, s.empresa_id, s.sucursal_id, s.tipo_documento, s.reinicio_periodo,
       f.periodo_clave, f.ultimo_numero
FROM compartido.series AS s
LEFT JOIN compartido.secuencias_folio AS f ON f.serie_id = s.id
WHERE s.activa AND s.tipo_documento IN (2, 3, 5, 6) AND s.reinicio_periodo <> 0
ORDER BY s.id, f.periodo_clave;

-- F6.1 · series de Venta directa (tipo 6) sin sucursal: la migración
-- SerieTipoVentaDirecta agrega ck_series_vd_sucursal y fallará si existen.
SELECT id, empresa_id, prefijo, activa
FROM compartido.series
WHERE tipo_documento = 6 AND sucursal_id IS NULL;
