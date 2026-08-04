-- =============================================================================
-- F8-PR3 — Seed 10k requisiciones para benchmark de bandejas
-- =============================================================================
--
-- Genera 10,000 filas en compras.requisiciones distribuidas entre estados,
-- departamentos y meses, para medir latencia de los endpoints
-- /api/v1/compras/requisiciones y /pendientes-autorizacion bajo carga
-- realista.
--
-- empresa_id: 00000003-0000-0000-0000-000000000001 (la inicial seedeada por
-- BootstrapSuperAdminHostedService cuando Auth:Bootstrap:EmpresaInicial está
-- en appsettings.Development.json).
--
-- Pool de departamentos / sucursales / requisitantes: GUIDs deterministas
-- generados con offset; permiten benchmarks reproducibles entre corridas.
--
-- Uso: psql -d millet_dev -f tools/perf/seed-10k-rqs.sql
-- Limpieza: psql -d millet_dev -c "DELETE FROM compras.requisiciones WHERE empresa_id = '00000003-0000-0000-0000-000000000001' AND folio LIKE 'PERF-%'"
-- =============================================================================

DO $$
DECLARE
    perf_empresa CONSTANT uuid := '00000003-0000-0000-0000-000000000001';
    perf_sucursal CONSTANT uuid := '00000003-0001-0000-0000-000000000001';
BEGIN
    -- Generador masivo: 10,000 filas con CTE.
    INSERT INTO compras.requisiciones (
        id, empresa_id, folio, folio_anio, clasificacion,
        sucursal_id, departamento_id, almacen_destino_id,
        requisitante_id, creador_id, descripcion, prioridad,
        fecha_solicitud, fecha_entrega_deseada, proveedor_sugerido_id,
        estado, motivo_terminacion_id, motivo_terminacion_texto,
        actor_terminacion_id, fecha_terminacion,
        version, created_at, updated_at, created_by, updated_by, deleted_at
    )
    SELECT
        gen_random_uuid(),
        perf_empresa,
        -- folio único: PERF-NNNNNN (zero-padded 6 dígitos)
        'PERF-' || lpad(n::text, 6, '0'),
        2026,
        -- clasificacion 0..3 cíclica
        (n % 4)::smallint,
        perf_sucursal,
        -- pool de 20 departamentos: cycle por (n mod 20)
        ('00000099-0001-0000-0000-' || lpad((n % 20)::text, 12, '0'))::uuid,
        ('00000099-0002-0000-0000-' || lpad(((n / 20) % 5)::text, 12, '0'))::uuid,
        -- pool de 100 requisitantes
        ('00000099-0003-0000-0000-' || lpad((n % 100)::text, 12, '0'))::uuid,
        ('00000099-0003-0000-0000-' || lpad((n % 100)::text, 12, '0'))::uuid,
        'Perf seed RQ #' || n,
        (n % 3)::smallint,
        -- fecha en últimos 90 días distribuida uniforme
        now() - (random() * interval '90 days'),
        NULL,
        NULL,
        -- estado distribuido: 30% Borrador (0), 30% EnAutorizacion (1),
        -- 20% Cerrada (5), 10% Cancelada (4), 10% Rechazada (2)
        CASE
            WHEN n % 10 < 3 THEN 0
            WHEN n % 10 < 6 THEN 1
            WHEN n % 10 < 8 THEN 5
            WHEN n % 10 < 9 THEN 4
            ELSE 2
        END::smallint,
        NULL, NULL, NULL, NULL,
        0,
        now() - (random() * interval '90 days'),
        now() - (random() * interval '90 days'),
        'perf-seed',
        'perf-seed',
        NULL
    FROM generate_series(1, 10000) AS n
    ON CONFLICT DO NOTHING;

    RAISE NOTICE 'Insertadas % filas en compras.requisiciones (empresa=%)',
        (SELECT count(*) FROM compras.requisiciones WHERE folio LIKE 'PERF-%'),
        perf_empresa;
END$$;

-- Asegurar autovacuum / estadísticas frescas para que el planner pick los
-- índices correctos en el benchmark.
ANALYZE compras.requisiciones;
