-- =============================================================================
-- Backfill one-shot de tesoreria.pasivo_pendiente_pago (TES-PR3)
-- Runbook patrón ADR-0044 · Decisión STOP #1: opción (a) del
-- 04-cuidados-infra §3.1 — query dirigida a CxP, menor acoplamiento.
-- =============================================================================
-- Una subscription de Service Bus solo recibe eventos POSTERIORES a su
-- creación: todo pasivo que CxP autorizó antes del deploy de TES-PR3 no
-- aparecerá en la bandeja de Tesorería. Este script proyecta esas facturas
-- una sola vez, con la MISMA forma que el listener (upsert por
-- factura_proveedor_id). Es IDEMPOTENTE: re-correrlo actualiza sin duplicar,
-- y las filas que el listener ya proyectó solo se refrescan con el estado
-- vigente de CxP (que es la fuente de verdad).
--
-- ORDEN DE OPERACIÓN (importante):
--   1. Deploy de infra (subscription tesoreria-subscription creada, what-if
--      primero) y deploy de app (listener corriendo).
--   2. Correr este script. Así no queda hueco: lo anterior lo cubre el
--      backfill, lo nuevo lo cubre el listener, y el dedupe absorbe el solape.
--
-- Uso (psql contra la BD del ambiente):
--   psql "$PG_CONN" -f backfill-pasivos-pendientes-tesoreria.sql
--
-- Verificación posterior:
--   SELECT count(*) FROM tesoreria.pasivo_pendiente_pago;
--   -- vs
--   SELECT count(*) FROM cuentas_por_pagar.facturas_proveedor
--   WHERE estado = 3
--     AND (total - anticipo_aplicado_total - nc_aplicadas_total - importe_pagado) > 0;

INSERT INTO tesoreria.pasivo_pendiente_pago
    (id, empresa_id, factura_proveedor_id, proveedor_id, orden_compra_id,
     monto_total, saldo_pendiente, moneda, tipo_cambio, fecha_vencimiento,
     uuid_cfdi, folio_proveedor, metodo_pago, recibido_en,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
SELECT
    gen_random_uuid(),
    f.empresa_id,
    f.id,
    f.proveedor_id,
    f.orden_compra_id,
    f.total,
    -- Mismo cálculo que PasivoAutorizadoParaPagoMapper de CxP: saldo neto
    -- de anticipos, NC aplicadas y pagos.
    f.total - f.anticipo_aplicado_total - f.nc_aplicadas_total - f.importe_pagado,
    f.moneda,
    f.tipo_cambio,
    f.fecha_vencimiento,
    CASE WHEN f.uuid_cfdi ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
         THEN f.uuid_cfdi::uuid ELSE NULL END,
    f.folio_proveedor,
    NULL,           -- metodo_pago: no disponible [T-G11]
    now(),
    1, now(), now(), 'backfill-tes-pr3', 'backfill-tes-pr3', NULL
FROM cuentas_por_pagar.facturas_proveedor f
WHERE f.estado = 3  -- EstadoPasivo.Autorizada
  AND f.deleted_at IS NULL
  AND (f.total - f.anticipo_aplicado_total - f.nc_aplicadas_total - f.importe_pagado) > 0
ON CONFLICT (factura_proveedor_id) DO UPDATE SET
    monto_total       = EXCLUDED.monto_total,
    saldo_pendiente   = EXCLUDED.saldo_pendiente,
    moneda            = EXCLUDED.moneda,
    tipo_cambio       = EXCLUDED.tipo_cambio,
    fecha_vencimiento = EXCLUDED.fecha_vencimiento,
    uuid_cfdi         = EXCLUDED.uuid_cfdi,
    folio_proveedor   = EXCLUDED.folio_proveedor,
    updated_at        = now(),
    updated_by        = 'backfill-tes-pr3';
