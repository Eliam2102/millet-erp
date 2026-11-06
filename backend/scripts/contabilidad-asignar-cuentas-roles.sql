-- 86e3za6ck: asigna a los roles del motor contable las cuentas reales que Contabilidad confirmó el 5-nov
-- (tabla «Cuentas por operación», respondida con el catálogo actualizado al 5-nov).
--
-- Orden:
--   1) Contabilidad importa el catálogo del 5-nov (Contabilidad > Catálogo > Importar). La importación es incremental:
--      crea solo las 9 cuentas nuevas y pasa por la autorización del lote.
--   2) Correr este script.
--   3) Reprocesar desde la bandeja los rechazos por «rol sin cuenta».
--
-- Reglas:
--   - Idempotente.
--   - Solo toca el rol base (sin sucursal ni moneda), y solo si está sin cuenta o con una cuenta de prueba
--     (código 990.* o DEMO-*).
--   - Nunca pisa una cuenta real que Contabilidad ya haya asignado en Roles de cuenta.
--   - La cuenta destino debe existir, estar activa y recibir movimientos (afectable). Si no, el rol queda como estaba
--     y el reporte del final lo muestra.
--   - Cada asignación deja su renglón en la bitácora del rol.
--
-- No toca:
--   - EFECTIVO_CAJA_COBRANZA, CAJA ni DEUDORES_FUNCIONARIOS_EMPLEADOS: la póliza del efectivo de caja sigue pendiente
--     con Contabilidad.
--   - RETENCIONES_POR_PAGAR ni RETENCIONES_CLIENTES: las retenciones siguen pendientes.
--   - BANCOS, BANCOS_MN, GASTO, CONSUMO ni INVENTARIO: dependen de otro catálogo. Ver docs/modulos/contabilidad/16-cuentas-reales-y-cuentas-dinamicas.md.
--
-- Uso: psql "$CONEXION" -v ON_ERROR_STOP=1 -f backend/scripts/contabilidad-asignar-cuentas-roles.sql
BEGIN;

CREATE TEMP TABLE cuentas_confirmadas (rol text PRIMARY KEY, cuenta text NOT NULL, renglon text NOT NULL) ON COMMIT DROP;
INSERT INTO cuentas_confirmadas (rol, cuenta, renglon) VALUES
    ('MERCANCIA_RECIBIDA_POR_FACTURAR', '201.05.00.00', 'renglones 2, 3, 5, 12 y 16'),
    ('IVA_ACREDITABLE_PENDIENTE',       '119.01.00.00', 'renglones 7, 13, 17 y S4'),
    ('PROVEEDORES',                     '201.01.00.00', 'renglones 8, 10, 14 y 20 (nacionales)'),
    ('ANTICIPO_A_PROVEEDORES',          '120.01.00.00', 'renglón 18 (nacionales)'),
    ('CLIENTES',                        '105.01.00.00', 'renglones 22, 31, 33, 35, 37, 39 y 45 (nacionales)'),
    ('VENTAS',                          '401.01.00.00', 'renglón 24'),
    ('IVA_TRASLADADO_NO_COBRADO',       '209.01.00.00', 'renglones 25, 28, 30 y S5'),
    ('ANTICIPOS_DE_CLIENTES',           '206.01.00.00', 'renglones 27 y 32 (nacionales)'),
    ('DESCUENTOS_SOBRE_VENTAS',         '402.01.00.00', 'renglón 29'),
    ('COBROS_TC_TD',                    '101.01.03.00', 'renglón 34 (cuenta nueva)'),
    ('COBROS_CHEQUE',                   '101.01.04.00', 'renglón 38 (cuenta nueva)'),
    ('MERMA',                           '504.25.02.00', 'renglón 51 (cuenta nueva)'),
    ('AJUSTE_INVENTARIO',               '504.25.02.00', 'renglones 50 y 54 (cuenta nueva)'),
    ('IVA_ACREDITABLE_PAGADO',          '118.01.01.00', 'renglones S3 y S8'),
    ('COMISIONES_BANCARIAS',            '701.10.00.00', 'renglón S7');

CREATE TEMP TABLE por_asignar ON COMMIT DROP AS
SELECT r.id, r.empresa_id, r.codigo AS rol, r.cuenta_id AS anterior, c.id AS cuenta_nueva, cc.cuenta, cc.renglon
FROM contabilidad.roles_cuenta r
JOIN cuentas_confirmadas cc ON cc.rol = r.codigo
JOIN contabilidad.cuentas_contables c
  ON c.empresa_id = r.empresa_id AND c.codigo = cc.cuenta AND c.deleted_at IS NULL
 AND c.estatus = 0 /* Activo */ AND c.tipo = 1 /* Afectable */
LEFT JOIN contabilidad.cuentas_contables actual ON actual.id = r.cuenta_id
WHERE r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL
  AND (r.vigente_hasta IS NULL OR r.vigente_hasta >= current_date)
  AND r.cuenta_id IS DISTINCT FROM c.id
  AND (r.cuenta_id IS NULL OR actual.codigo LIKE '990.%' OR actual.codigo LIKE 'DEMO%');

UPDATE contabilidad.roles_cuenta r
   SET cuenta_id = p.cuenta_nueva, version = r.version + 1,
       updated_at = now(), updated_by = 'script-86e3za6ck'
  FROM por_asignar p
 WHERE r.id = p.id;

INSERT INTO contabilidad.roles_cuenta_cambios (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
SELECT gen_random_uuid(), empresa_id, id, anterior, cuenta_nueva, 'script-86e3za6ck', now(),
       'Cuenta confirmada por Contabilidad el 5-nov (' || renglon || ')'
FROM por_asignar;

-- Reporte: qué quedó en cada rol de la lista.
SELECT cc.rol, cc.cuenta AS cuenta_confirmada, actual.codigo AS cuenta_del_rol,
       CASE WHEN p.id IS NOT NULL THEN 'asignada ahora'
            WHEN actual.codigo = cc.cuenta THEN 'ya estaba'
            WHEN r.id IS NULL THEN 'no hay rol base vigente (sin sucursal ni moneda)'
            WHEN NOT EXISTS (SELECT 1 FROM contabilidad.cuentas_contables c WHERE c.empresa_id = r.empresa_id
                             AND c.codigo = cc.cuenta AND c.deleted_at IS NULL AND c.estatus = 0 AND c.tipo = 1)
                 THEN 'falta la cuenta en el catálogo (importar el del 5-nov)'
            ELSE 'no se tocó: Contabilidad ya le asignó otra cuenta real' END AS resultado
FROM cuentas_confirmadas cc
LEFT JOIN contabilidad.roles_cuenta r ON r.codigo = cc.rol AND r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL
                                    AND (r.vigente_hasta IS NULL OR r.vigente_hasta >= current_date)
LEFT JOIN contabilidad.cuentas_contables actual ON actual.id = r.cuenta_id
LEFT JOIN por_asignar p ON p.id = r.id
ORDER BY cc.rol;

COMMIT;
