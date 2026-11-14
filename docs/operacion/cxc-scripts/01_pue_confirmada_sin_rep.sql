-- =====================================================================================================================
-- H-U49 · Pagos confirmados por Tesorería sobre facturas PUE que quedaron esperando un REP (que nunca se emite)
-- =====================================================================================================================
-- Contexto: antes de esta corrección, al confirmar un depósito contra una factura PUE:
--   * Facturación creaba un pendiente de REP (facturacion.repp_pendiente) imposible de emitir (el REP solo aplica a PPD),
--     y ese pendiente bloqueaba el estado de cuenta del cliente.
--   * CxC dejaba la propuesta «confirmada, REP pendiente» y no aplicaba el pago a cartera (la factura seguía abierta).
-- Desde la corrección, las confirmaciones nuevas ya aplican la PUE a cartera y no crean pendiente. Este script es SOLO para
-- los casos anteriores. NO se corre automático: se corre a mano, tras revisar el diagnóstico, por quien administre la base.
--
-- Póliza: la del cobro (pago de cliente, Bancos contra Clientes) ya la generó Contabilidad con la confirmación de
-- Tesorería; este script no toca Contabilidad ni genera pólizas. Solo alinea cartera y la bandeja de REP.
--
-- Pasos:
--   1. Correr la sección DIAGNÓSTICO y revisar cada fila (folio, cliente, importes).
--   2. Si una factura aparece con «cobro_caja» > 0, revisar antes: se cobró también en caja (posible doble cobro). Esas
--      filas el reproceso las aplica solo hasta el saldo; un saldo 0 no se toca y hay que resolverlo con Tesorería/Caja.
--   3. Correr la sección REPROCESO completa. Termina en ROLLBACK: revisar la verificación y, si es correcta, volver a
--      correrla cambiando ROLLBACK por COMMIT.
--   4. Tras el COMMIT, el bloqueo por vencimiento del cliente (si lo había) se reevalúa con el siguiente pago o con la
--      evaluación de alertas de cartera; no se recalcula aquí.
-- =====================================================================================================================


-- ============================================ DIAGNÓSTICO (solo lectura) ============================================

-- D1. Líneas PUE de propuestas confirmadas sin cerrar, con lo que el reproceso aplicaría.
SELECT p.id                                         AS propuesta_id,
       p.deposito_ref,
       p.resuelta_en                                AS confirmada_en,
       fc.folio,
       fc.receptor_nombre,
       fc.metodo_pago,
       l.importe_aplicado,
       fc.total - fc.monto_pagado - fc.monto_nc     AS saldo_cartera,
       LEAST(l.importe_aplicado, fc.total - fc.monto_pagado - fc.monto_nc) AS se_aplicaria,
       (SELECT COALESCE(SUM(m.importe), 0) FROM cuentas_por_cobrar.movimiento_cartera m
         WHERE m.factura_cartera_id = fc.id AND m.tipo = 1 AND NOT m.revertido AND m.deleted_at IS NULL
           AND m.origen_comprobante_id <> p.id)     AS otros_pagos_en_cartera,
       (SELECT COALESCE(SUM(c.total), 0) FROM facturacion.cobro_mostrador c
         WHERE c.comprobante_id = fc.factura_venta_id AND c.estado = 1) AS cobro_caja   -- 1 = Registrado
FROM cuentas_por_cobrar.propuesta_aplicacion_pago p
JOIN cuentas_por_cobrar.propuesta_aplicacion_factura l ON l.propuesta_id = p.id AND l.deleted_at IS NULL
JOIN cuentas_por_cobrar.factura_cartera fc ON fc.id = l.factura_cartera_id
WHERE p.estado = 2                -- Confirmada
  AND NOT p.repp_timbrado
  AND p.deleted_at IS NULL
  AND upper(fc.metodo_pago) <> 'PPD'
  AND NOT EXISTS (SELECT 1 FROM cuentas_por_cobrar.movimiento_cartera m
                   WHERE m.origen_comprobante_id = p.id AND m.factura_cartera_id = fc.id AND m.tipo = 1)
ORDER BY p.resuelta_en;

-- D2. Pendientes de REP que solo relacionan facturas PUE (ya no bloquean el estado de cuenta; se descartan en el reproceso).
SELECT r.id, r.cliente_id, r.monto, r.fecha_valor, r.referencia
FROM facturacion.repp_pendiente r
WHERE r.estado = 'Pendiente'
  AND EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f WHERE f.repp_pendiente_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f
                   JOIN facturacion.comprobante c ON c.id = f.factura_venta_id
                   WHERE f.repp_pendiente_id = r.id AND upper(c.metodo_pago) = 'PPD')
  AND NOT EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f
                   LEFT JOIN facturacion.comprobante c ON c.id = f.factura_venta_id
                   WHERE f.repp_pendiente_id = r.id AND c.id IS NULL);


-- ==================================================== REPROCESO ====================================================

BEGIN;

CREATE TEMP TABLE hu49_lineas ON COMMIT DROP AS
SELECT p.id AS propuesta_id, p.empresa_id, p.resuelta_en, fc.id AS factura_cartera_id,
       LEAST(l.importe_aplicado, fc.total - fc.monto_pagado - fc.monto_nc) AS importe
FROM cuentas_por_cobrar.propuesta_aplicacion_pago p
JOIN cuentas_por_cobrar.propuesta_aplicacion_factura l ON l.propuesta_id = p.id AND l.deleted_at IS NULL
JOIN cuentas_por_cobrar.factura_cartera fc ON fc.id = l.factura_cartera_id
WHERE p.estado = 2 AND NOT p.repp_timbrado AND p.deleted_at IS NULL
  AND fc.estado <> 4                                   -- no cancelada
  AND upper(fc.metodo_pago) <> 'PPD'
  AND NOT EXISTS (SELECT 1 FROM cuentas_por_cobrar.movimiento_cartera m
                   WHERE m.origen_comprobante_id = p.id AND m.factura_cartera_id = fc.id AND m.tipo = 1);

-- R1. Movimiento de pago por línea, con origen = propuesta (igual que la aplicación nueva). Saldo 0 → no se aplica.
INSERT INTO cuentas_por_cobrar.movimiento_cartera
       (id, empresa_id, factura_cartera_id, tipo, origen_comprobante_id, importe, fecha_movimiento,
        revertido, created_at, created_by, updated_at, updated_by, version)
SELECT gen_random_uuid(), empresa_id, factura_cartera_id, 1, propuesta_id, importe, resuelta_en,
       false, now(), 'script H-U49', now(), 'script H-U49', 0
FROM hu49_lineas
WHERE importe > 0;

-- R2. Monto pagado y estado de la factura (Abierta 1 / Parcial 2 / Pagada 3), como FacturaCartera.AplicarPago.
UPDATE cuentas_por_cobrar.factura_cartera fc
SET monto_pagado = fc.monto_pagado + x.importe,
    estado = CASE WHEN fc.monto_pagado + x.importe + fc.monto_nc <= 0 THEN 1
                  WHEN fc.monto_pagado + x.importe + fc.monto_nc < fc.total THEN 2
                  ELSE 3 END,
    updated_at = now(), updated_by = 'script H-U49', version = fc.version + 1
FROM (SELECT factura_cartera_id, SUM(importe) AS importe FROM hu49_lineas WHERE importe > 0 GROUP BY factura_cartera_id) x
WHERE fc.id = x.factura_cartera_id;

-- R3. Cerrar las propuestas cuyas facturas son todas PUE (ya no esperan REP).
UPDATE cuentas_por_cobrar.propuesta_aplicacion_pago p
SET repp_timbrado = true, updated_at = now(), updated_by = 'script H-U49', version = p.version + 1
WHERE p.id IN (SELECT DISTINCT propuesta_id FROM hu49_lineas)
  AND NOT EXISTS (SELECT 1 FROM cuentas_por_cobrar.propuesta_aplicacion_factura l
                   JOIN cuentas_por_cobrar.factura_cartera fc ON fc.id = l.factura_cartera_id
                   WHERE l.propuesta_id = p.id AND l.deleted_at IS NULL AND upper(fc.metodo_pago) = 'PPD');

-- R4. Descartar los pendientes de REP que solo relacionan PUE (mismo criterio que D2).
UPDATE facturacion.repp_pendiente r
SET estado = 'Descartado', motivo_descarte = 'H-U49: solo facturas PUE; fiscalmente no lleva REP',
    updated_at = now(), updated_by = 'script H-U49', version = r.version + 1
WHERE r.estado = 'Pendiente'
  AND EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f WHERE f.repp_pendiente_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f
                   JOIN facturacion.comprobante c ON c.id = f.factura_venta_id
                   WHERE f.repp_pendiente_id = r.id AND upper(c.metodo_pago) = 'PPD')
  AND NOT EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f
                   LEFT JOIN facturacion.comprobante c ON c.id = f.factura_venta_id
                   WHERE f.repp_pendiente_id = r.id AND c.id IS NULL);

-- V1. Verificación: ninguna factura con más pagado que su total, y lo aplicado coincide con lo calculado.
SELECT 'sobrepago' AS chequeo, count(*) AS filas
FROM cuentas_por_cobrar.factura_cartera WHERE monto_pagado + monto_nc > total
UNION ALL
SELECT 'movimientos_creados', count(*) FROM cuentas_por_cobrar.movimiento_cartera WHERE created_by = 'script H-U49'
UNION ALL
SELECT 'lineas_con_importe', count(*) FROM hu49_lineas WHERE importe > 0
UNION ALL
SELECT 'lineas_sin_saldo_revisar', count(*) FROM hu49_lineas WHERE importe <= 0
UNION ALL
SELECT 'pendientes_pue_restantes', count(*) FROM facturacion.repp_pendiente r
WHERE r.estado = 'Pendiente'
  AND EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f WHERE f.repp_pendiente_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM facturacion.repp_pendiente_factura f
                   JOIN facturacion.comprobante c ON c.id = f.factura_venta_id
                   WHERE f.repp_pendiente_id = r.id AND upper(c.metodo_pago) = 'PPD');
-- Esperado: sobrepago = 0; movimientos_creados = lineas_con_importe; pendientes_pue_restantes = 0.
-- lineas_sin_saldo_revisar > 0 → facturas PUE ya saldadas por otra vía (p. ej. caja): revisar con Tesorería/Caja.

ROLLBACK;   -- Cambiar por COMMIT solo después de revisar la verificación.
