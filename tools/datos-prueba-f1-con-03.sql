-- F1-CON-03 · Datos de PRUEBA para periodos contables (solo ambientes locales/dev).
--
-- NO es el calendario oficial de Contabilidad de Millet (dependencia externa por confirmar): crea el ejercicio DEMO 2026 con
-- enero a diciembre abiertos y el periodo 13 sin abrir, para que el movimiento de prueba de CON-02 («Probar movimiento») siga
-- operando con la verificación de periodo (falla cerrada, D9). Equivale a POST /periodos/ejercicios + POST /ejercicios/{id}/abrir.
-- Todo lo sembrado queda marcado con created_by = 'seed-f1-con-03-prueba' para retirarlo con la sección final.
--
-- Uso:   docker exec -i millet-dev-postgres psql -U pgadmin -d millet_dev < tools/datos-prueba-f1-con-03.sql
-- Idempotente: si el ejercicio 2026 ya existe (creado por la API o por este script) no hace nada.

DO $$
DECLARE
    v_empresa   uuid;
    v_marca     text := 'seed-f1-con-03-prueba';
    v_anio      int  := 2026;
    v_ejercicio uuid := gen_random_uuid();
BEGIN
    SELECT id INTO v_empresa FROM compartido.empresas ORDER BY created_at LIMIT 1;
    IF v_empresa IS NULL THEN RAISE EXCEPTION 'No hay empresa: corre primero el bootstrap del API.'; END IF;
    IF EXISTS (SELECT 1 FROM contabilidad.ejercicios_contables WHERE empresa_id = v_empresa AND anio = v_anio) THEN
        RAISE NOTICE 'El ejercicio % ya existe; no se siembra nada.', v_anio;
        RETURN;
    END IF;

    INSERT INTO contabilidad.ejercicios_contables (id, empresa_id, anio, version, created_at, updated_at, created_by, updated_by)
    VALUES (v_ejercicio, v_empresa, v_anio, 1, now(), now(), v_marca, v_marca);

    -- 1–12: meses, abiertos (versión 2 = creado + abierto). 13: ajustes de auditoría (31-dic), sin abrir.
    INSERT INTO contabilidad.periodos_contables (id, empresa_id, ejercicio_id, anio, numero, fecha_inicio, fecha_fin, estado,
                                                 abierto_por, abierto_en, version, created_at, updated_at, created_by, updated_by)
    SELECT gen_random_uuid(), v_empresa, v_ejercicio, v_anio, n,
           CASE WHEN n = 13 THEN make_date(v_anio, 12, 31) ELSE make_date(v_anio, n, 1) END,
           CASE WHEN n = 13 THEN make_date(v_anio, 12, 31) ELSE (make_date(v_anio, n, 1) + interval '1 month' - interval '1 day')::date END,
           CASE WHEN n = 13 THEN 0 ELSE 1 END,
           CASE WHEN n = 13 THEN NULL ELSE v_marca END,
           CASE WHEN n = 13 THEN NULL ELSE now() END,
           CASE WHEN n = 13 THEN 1 ELSE 2 END,
           now(), now(), v_marca, v_marca
    FROM generate_series(1, 13) AS n;

    -- Bitácora de la apertura (misma forma que la escribe la API).
    INSERT INTO contabilidad.periodos_contables_bitacora (id, empresa_id, periodo_id, accion, estado_anterior, estado_nuevo, motivo,
                                                          usuario_id, usuario_nombre, ocurrido_en, version_resultante, version,
                                                          created_at, updated_at, created_by, updated_by)
    SELECT gen_random_uuid(), v_empresa, p.id, 1, 0, 1, 'FIX apertura DEMO del ejercicio 2026 (datos de prueba)',
           NULL, v_marca, now(), 2, 1, now(), now(), v_marca, v_marca
    FROM contabilidad.periodos_contables p
    WHERE p.ejercicio_id = v_ejercicio AND p.numero <= 12;
END $$;

-- ─── RETIRO (descomentar y ejecutar para quitar todo lo sembrado por este script) ───────────────────────────────────
-- DELETE FROM contabilidad.periodos_contables_bitacora WHERE periodo_id IN (SELECT id FROM contabilidad.periodos_contables WHERE created_by = 'seed-f1-con-03-prueba');
-- DELETE FROM contabilidad.periodos_contables WHERE created_by = 'seed-f1-con-03-prueba';
-- DELETE FROM contabilidad.ejercicios_contables WHERE created_by = 'seed-f1-con-03-prueba';
