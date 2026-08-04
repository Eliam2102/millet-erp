-- =============================================================================
-- Seed one-shot de tesoreria.cuenta_bancaria (TES-PR1, patrón runbook ADR-0044)
-- =============================================================================
-- Los números de cuenta / CLABE reales NO van en el repo (cuidados-infra
-- §3.3, mismo criterio que ADR-0037): este script se corre a mano en cada
-- ambiente sustituyendo los placeholders por los datos reales de las
-- cuentas operativas de Millet. La lista definitiva de bancos/cuentas está
-- pendiente de Javier Canche [T-G2] — el script es ITERABLE: el ON CONFLICT
-- por (empresa_id, numero_cuenta) permite re-correrlo para agregar cuentas
-- o corregir datos sin duplicar filas.
--
-- TES-7 revisada (2026-07-15): ya existe CRUD de cuentas en el módulo
-- (POST/PUT /api/v1/tesoreria/cuentas, permiso
-- tesoreria.cuentas.administrar). Este script queda solo para bootstrap
-- masivo de ambientes nuevos; el alta cotidiana va por la UI.
--
-- Uso (psql contra la BD del ambiente):
--   1. Obtener el empresa_id real:  SELECT id, nombre FROM administracion.empresas;
--   2. Sustituir :empresa_id y los placeholders <...> de cada fila.
--   3. Ejecutar. Verificar con:  SELECT banco, numero_cuenta, moneda, activa
--                                FROM tesoreria.cuenta_bancaria;
--
-- perfil_extracto queda NULL hasta que los perfiles de parser existan
-- (TES-PR9, gate T-G2). cuenta_contable_ref queda NULL hasta Contabilidad.

INSERT INTO tesoreria.cuenta_bancaria
    (id, empresa_id, banco, numero_cuenta, clabe, moneda,
     cuenta_contable_ref, perfil_extracto, activa,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
VALUES
    -- Placeholder 1: cuenta operativa MXN (sustituir por datos reales)
    (gen_random_uuid(), :'empresa_id', '<BANCO_1>', '<NUMERO_CUENTA_1>', '<CLABE_1>', 'MXN',
     NULL, NULL, true,
     1, now(), now(), 'seed-script', 'seed-script', NULL),
    -- Placeholder 2: cuenta operativa USD (sustituir o eliminar)
    (gen_random_uuid(), :'empresa_id', '<BANCO_2>', '<NUMERO_CUENTA_2>', '<CLABE_2>', 'USD',
     NULL, NULL, true,
     1, now(), now(), 'seed-script', 'seed-script', NULL)
ON CONFLICT (empresa_id, numero_cuenta) DO UPDATE SET
    banco       = EXCLUDED.banco,
    clabe       = EXCLUDED.clabe,
    moneda      = EXCLUDED.moneda,
    activa      = EXCLUDED.activa,
    updated_at  = now(),
    updated_by  = 'seed-script';
