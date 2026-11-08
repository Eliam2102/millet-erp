-- 86e3zcbjw: asigna a los roles de retención por concepto la cuenta de la tabla de retenciones de Contabilidad (5-nov),
-- traducida del código SAP al catálogo actualizado al 5-nov.
--
-- Roles (los crea la migración C17RetencionesPorConcepto, sin cuenta):
--   - RETENCIONES_POR_PAGAR_{CONCEPTO}_{IMPUESTO}: retenciones a proveedores; se abonan al pagar la factura.
--   - RETENCIONES_CLIENTES_{CONCEPTO}_{IMPUESTO}: retenciones que nos hacen los clientes; se cargan con la factura de venta.
--   Sin cuenta en el rol del concepto, el motor usa el rol base (RETENCIONES_POR_PAGAR o RETENCIONES_CLIENTES).
--
-- Orden:
--   1) Catálogo del 5-nov importado (ver backend/scripts/contabilidad-asignar-cuentas-roles.sql).
--   2) Correr este script.
--   3) Reprocesar desde la bandeja los rechazos por «rol sin cuenta».
--
-- Reglas (mismas que contabilidad-asignar-cuentas-roles.sql):
--   - Idempotente.
--   - Solo toca el rol vigente sin sucursal ni moneda, y solo si está sin cuenta o con una cuenta de prueba (990.* o DEMO-*).
--   - Nunca pisa una cuenta real que Contabilidad ya haya asignado en Roles de cuenta.
--   - La cuenta destino debe existir, estar activa y ser afectable.
--   - Cada asignación deja su renglón en la bitácora del rol.
--
-- Conceptos sin cuenta en el catálogo del 5-nov (el reporte los marca «pendiente»): Contabilidad da de alta la cuenta y
-- la asigna en Roles de cuenta (o se agrega aquí y se vuelve a correr).
--
-- Uso: psql "$CONEXION" -v ON_ERROR_STOP=1 -f backend/scripts/contabilidad-asignar-cuentas-retenciones.sql
BEGIN;

CREATE TEMP TABLE cuentas_retencion (rol text PRIMARY KEY, cuenta text, sap text NOT NULL, retencion text NOT NULL) ON COMMIT DROP;
INSERT INTO cuentas_retencion (rol, cuenta, sap, retencion) VALUES
    -- Retenciones a proveedores (se descuentan al pagar)
    ('RETENCIONES_POR_PAGAR_HONORARIOS_PF_ISR',          '216.04.00.00', '211601040000-00-0', 'ISR servicios profesionales 10 %'),
    ('RETENCIONES_POR_PAGAR_HONORARIOS_PF_IVA',          '216.10.01.00', '211602020001-00-0', 'IVA honorarios 10.6667 %'),
    ('RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_ISR',       '216.03.00.00', '211601030000-00-0', 'ISR arrendamiento 10 %'),
    ('RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_IVA',       '216.02.03.00', '211602030001-00-0', 'IVA arrendamientos 10.6667 %'),
    ('RETENCIONES_POR_PAGAR_FLETES_IVA',                 '216.10.02.00', '211602010001-00-0', 'IVA fletes pagado 4 %'),
    ('RETENCIONES_POR_PAGAR_SERVICIOS_IVA',              '216.02.04.00', '211602040001-00-0', 'IVA servicios pagado 6 %'),
    ('RETENCIONES_POR_PAGAR_RESICO_PF_ISR',              '216.01.05.00', '211601050000-00-0', 'ISR RESICO 1.25 %'),
    ('RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_ISR',    '216.01.05.00', '211601050000-00-0', 'ISR RESICO 1.25 %'),
    ('RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_IVA',    '216.02.02.02', '211602020002-00-0', 'IVA RESICO 10.6667 %'),
    -- Retenciones que nos hacen los clientes (se registran con la factura de venta)
    ('RETENCIONES_CLIENTES_FLETES_IVA',                  '113.01.02.01', '111308000001-00-0', 'IVA fletes cobrado 4 %'),
    ('RETENCIONES_CLIENTES_SERVICIOS_IVA',               '113.01.02.03', '111308000005-00-0', 'IVA servicios cobrado 6 %'),
    ('RETENCIONES_CLIENTES_DESPERDICIO_IVA',             '113.01.02.05', '111308000008-00-0', 'IVA desperdicio cobrado 16 %'),
    ('RETENCIONES_CLIENTES_IMPORTACION_IVA',             '113.01.02.07', '111308000011-00-0', 'IVA importación 16 %');

CREATE TEMP TABLE por_asignar ON COMMIT DROP AS
SELECT r.id, r.empresa_id, r.codigo AS rol, r.cuenta_id AS anterior, c.id AS cuenta_nueva, cr.cuenta, cr.sap, cr.retencion
FROM contabilidad.roles_cuenta r
JOIN cuentas_retencion cr ON cr.rol = r.codigo AND cr.cuenta IS NOT NULL
JOIN contabilidad.cuentas_contables c
  ON c.empresa_id = r.empresa_id AND c.codigo = cr.cuenta AND c.deleted_at IS NULL
 AND c.estatus = 0 /* Activo */ AND c.tipo = 1 /* Afectable */
LEFT JOIN contabilidad.cuentas_contables actual ON actual.id = r.cuenta_id
WHERE r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL
  AND (r.vigente_hasta IS NULL OR r.vigente_hasta >= current_date)
  AND r.cuenta_id IS DISTINCT FROM c.id
  AND (r.cuenta_id IS NULL OR actual.codigo LIKE '990.%' OR actual.codigo LIKE 'DEMO%');

UPDATE contabilidad.roles_cuenta r
   SET cuenta_id = p.cuenta_nueva, version = r.version + 1,
       updated_at = now(), updated_by = 'script-86e3zcbjw'
  FROM por_asignar p
 WHERE r.id = p.id;

INSERT INTO contabilidad.roles_cuenta_cambios (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
SELECT gen_random_uuid(), empresa_id, id, anterior, cuenta_nueva, 'script-86e3zcbjw', now(),
       'Tabla de retenciones de Contabilidad del 5-nov: ' || retencion || ' (SAP ' || sap || ')'
FROM por_asignar;

-- Reporte: qué quedó en cada rol de retención.
SELECT cr.rol, cr.retencion, cr.sap, cr.cuenta AS cuenta_catalogo, actual.codigo AS cuenta_del_rol,
       CASE WHEN p.id IS NOT NULL THEN 'asignada ahora'
            WHEN cr.cuenta IS NULL AND actual.codigo IS NULL THEN 'pendiente: el catálogo del 5-nov no tiene la cuenta; usa el rol base'
            WHEN cr.cuenta IS NULL THEN 'Contabilidad ya le asignó cuenta'
            WHEN actual.codigo = cr.cuenta THEN 'ya estaba'
            WHEN r.id IS NULL THEN 'no hay rol vigente (correr la migración C17)'
            WHEN NOT EXISTS (SELECT 1 FROM contabilidad.cuentas_contables c WHERE c.empresa_id = r.empresa_id
                             AND c.codigo = cr.cuenta AND c.deleted_at IS NULL AND c.estatus = 0 AND c.tipo = 1)
                 THEN 'falta la cuenta en el catálogo (importar el del 5-nov)'
            ELSE 'no se tocó: Contabilidad ya le asignó otra cuenta real' END AS resultado
FROM cuentas_retencion cr
LEFT JOIN contabilidad.roles_cuenta r ON r.codigo = cr.rol AND r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL
                                    AND (r.vigente_hasta IS NULL OR r.vigente_hasta >= current_date)
LEFT JOIN contabilidad.cuentas_contables actual ON actual.id = r.cuenta_id
LEFT JOIN por_asignar p ON p.id = r.id
ORDER BY cr.rol;

COMMIT;
