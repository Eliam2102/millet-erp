-- F1-CON-02 · Datos de PRUEBA para dimensiones contables (solo ambientes locales/dev).
--
-- NO son política de Contabilidad de Millet: sirven para recorrer la pantalla y la API mientras llegan los insumos
-- M1 (centros por sucursal), M2 (tipos de documento) y M3 (matriz de reglas). Todo lo sembrado queda marcado con
-- created_by = 'seed-f1-con-02-prueba' (y es_prueba = true donde existe la columna) para retirarlo con la sección final.
--
-- Uso:   docker exec -i millet-dev-postgres psql -U pgadmin -d millet_dev < tools/datos-prueba-f1-con-02.sql
-- Retiro: descomentar y ejecutar el bloque "RETIRO" del final.
--
-- Sobre los datos base existentes:
--   * Centros: usa los CeCo (Dim2) activos ya sembrados por ADM-08; no crea centros.
--   * Ubicaciones: liga cada ubicación (Dim1) a la sucursal con su misma clave (101–105) o, en local, a las sucursales de
--     prueba en orden; marca como corporativos los CeCo de Administración y Finanzas, Logística y Abasto de 101.
--   * Cuentas: si el catálogo ya tiene cuentas reales (CON-01) solo crea una rama FIX aparte; nunca toca las reales.
-- Idempotente: se puede correr varias veces.

DO $$
DECLARE
    v_empresa   uuid;
    v_hoy       date := (now() AT TIME ZONE 'America/Merida')::date;
    v_marca     text := 'seed-f1-con-02-prueba';
    v_fp        uuid;
    v_em        uuid;
    v_am        uuid;
    v_raiz      uuid;
    v_mant      uuid;
    v_pap       uuid;
BEGIN
    SELECT id INTO v_empresa FROM compartido.empresas ORDER BY created_at LIMIT 1;
    IF v_empresa IS NULL THEN RAISE EXCEPTION 'No hay empresa: corre primero el bootstrap del API.'; END IF;

    -- Tipos de documento de prueba (M2 pendiente).
    INSERT INTO contabilidad.tipos_documento_contable (id, empresa_id, clave, nombre, estatus, es_prueba, version, created_at, updated_at, created_by, updated_by)
    VALUES (gen_random_uuid(), v_empresa, 'FIX-FP', 'FIX Factura de proveedor (prueba)', 0, true, 1, now(), now(), v_marca, v_marca),
           (gen_random_uuid(), v_empresa, 'FIX-EM', 'FIX Entrada de mercancía (prueba)', 0, true, 1, now(), now(), v_marca, v_marca),
           (gen_random_uuid(), v_empresa, 'FIX-AM', 'FIX Asiento manual (prueba)', 0, true, 1, now(), now(), v_marca, v_marca)
    ON CONFLICT (empresa_id, clave) DO NOTHING;
    SELECT id INTO v_fp FROM contabilidad.tipos_documento_contable WHERE empresa_id = v_empresa AND clave = 'FIX-FP';
    SELECT id INTO v_em FROM contabilidad.tipos_documento_contable WHERE empresa_id = v_empresa AND clave = 'FIX-EM';
    SELECT id INTO v_am FROM contabilidad.tipos_documento_contable WHERE empresa_id = v_empresa AND clave = 'FIX-AM';

    -- Rama de cuentas FIX (tipo 0 = acumula, 1 = afectable; naturaleza 0 = deudora).
    INSERT INTO contabilidad.cuentas_contables (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control, clase, version, created_at, updated_at, created_by, updated_by)
    VALUES (gen_random_uuid(), v_empresa, 'FIX-600', 'FIX Gastos de operación (prueba)', NULL, 1, 0, 0, 0, 0, 0, 1, now(), now(), v_marca, v_marca)
    ON CONFLICT (empresa_id, codigo) DO NOTHING;
    SELECT id INTO v_raiz FROM contabilidad.cuentas_contables WHERE empresa_id = v_empresa AND codigo = 'FIX-600';
    INSERT INTO contabilidad.cuentas_contables (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control, clase, version, created_at, updated_at, created_by, updated_by)
    VALUES (gen_random_uuid(), v_empresa, 'FIX-600.01', 'FIX Mantenimiento de maquinaria (prueba)', v_raiz, 2, 0, 1, 0, 0, 0, 1, now(), now(), v_marca, v_marca),
           (gen_random_uuid(), v_empresa, 'FIX-600.02', 'FIX Papelería y artículos de oficina (prueba)', v_raiz, 2, 0, 1, 0, 0, 0, 1, now(), now(), v_marca, v_marca)
    ON CONFLICT (empresa_id, codigo) DO NOTHING;
    SELECT id INTO v_mant FROM contabilidad.cuentas_contables WHERE empresa_id = v_empresa AND codigo = 'FIX-600.01';
    SELECT id INTO v_pap FROM contabilidad.cuentas_contables WHERE empresa_id = v_empresa AND codigo = 'FIX-600.02';

    -- Reglas de prueba (dimension 1..3 = Dim1..Dim3; requerimiento 1 obligatorio, 2 opcional, 3 no aplica):
    --   * Rama FIX-600, todos los tipos: Dimensión 2 (CeCo) obligatoria → la heredan sus cuentas.
    --   * FIX-600.01 en factura de proveedor: Dimensión 3 (equipo) obligatoria.
    --   * FIX-600.02, todos los tipos: Dimensión 3 no aplica.
    INSERT INTO contabilidad.reglas_dimension (id, empresa_id, cuenta_id, tipo_documento_id, dimension, requerimiento, vigente_desde, vigente_hasta, es_prueba, nota, version, created_at, updated_at, created_by, updated_by)
    VALUES (gen_random_uuid(), v_empresa, v_raiz, NULL, 2, 1, v_hoy, NULL, true, 'Prueba: todo gasto de operación pide CeCo', 1, now(), now(), v_marca, v_marca),
           (gen_random_uuid(), v_empresa, v_mant, v_fp, 3, 1, v_hoy, NULL, true, 'Prueba: mantenimiento facturado pide el equipo', 1, now(), now(), v_marca, v_marca),
           (gen_random_uuid(), v_empresa, v_pap, NULL, 3, 3, v_hoy, NULL, true, 'Prueba: papelería no se asigna a equipo', 1, now(), now(), v_marca, v_marca)
    ON CONFLICT (empresa_id, cuenta_id, tipo_documento_id, dimension, vigente_desde) DO NOTHING;

    -- Ubicación (Dim1) → sucursal (V49, supuesto de K10.2): si existe una sucursal con la MISMA clave que la ubicación
    -- (101 Conkal … 105 Planta Pintura, como en el Excel de insumos de Millet) se usa esa; si no (BD local con sucursales
    -- de prueba), se ligan en orden de clave las que alcancen y las demás quedan sin sucursal (sirve para ver ese caso).
    INSERT INTO contabilidad.ubicaciones_sucursal (id, empresa_id, dim1_id, sucursal_id, version, created_at, updated_at, created_by, updated_by)
    SELECT gen_random_uuid(), v_empresa, u.id, COALESCE(igual.id, orden.id), 1, now(), now(), v_marca, v_marca
    FROM (SELECT id, clave, row_number() OVER (ORDER BY clave) n FROM centros_costo.dim1 WHERE estatus = 0) u
    LEFT JOIN compartido.sucursales igual ON igual.empresa_id = v_empresa AND igual.clave = u.clave AND igual.estatus = 0
    LEFT JOIN (SELECT id, row_number() OVER (ORDER BY clave) n FROM compartido.sucursales WHERE empresa_id = v_empresa AND estatus = 0) orden
           ON orden.n = u.n
    WHERE COALESCE(igual.id, orden.id) IS NOT NULL
    ON CONFLICT (empresa_id, dim1_id) DO NOTHING;

    -- Centros corporativos (V49, supuesto): Administración y Finanzas (40…), Logística y Abasto (21…, 22…, 23…) de la ubicación 101.
    INSERT INTO contabilidad.centros_corporativos (id, empresa_id, dim2_id, version, created_at, updated_at, created_by, updated_by)
    SELECT gen_random_uuid(), v_empresa, d2.id, 1, now(), now(), v_marca, v_marca
    FROM centros_costo.dim2 d2 JOIN centros_costo.dim1 d1 ON d1.id = d2.dim1_id
    WHERE d2.estatus = 0 AND d1.clave = '101' AND left(d2.clave, 2) IN ('40', '21', '22', '23')
    ON CONFLICT (empresa_id, dim2_id) DO NOTHING;

    RAISE NOTICE 'F1-CON-02 datos de prueba listos (empresa %, hoy %).', v_empresa, v_hoy;
END $$;

-- ─── RETIRO (descomentar y ejecutar para quitar todo lo sembrado por este script) ───────────────────────────────────
-- DELETE FROM contabilidad.movimientos_dimension_prueba WHERE cuenta_codigo LIKE 'FIX-600%';
-- DELETE FROM contabilidad.reglas_dimension_uso WHERE regla_id IN (SELECT id FROM contabilidad.reglas_dimension WHERE created_by = 'seed-f1-con-02-prueba');
-- DELETE FROM contabilidad.reglas_dimension WHERE created_by = 'seed-f1-con-02-prueba';
-- DELETE FROM contabilidad.ubicaciones_sucursal WHERE created_by = 'seed-f1-con-02-prueba';
-- DELETE FROM contabilidad.centros_corporativos WHERE created_by = 'seed-f1-con-02-prueba';
-- DELETE FROM contabilidad.cuentas_contables WHERE created_by = 'seed-f1-con-02-prueba' AND padre_id IS NOT NULL;
-- DELETE FROM contabilidad.cuentas_contables WHERE created_by = 'seed-f1-con-02-prueba';
-- DELETE FROM contabilidad.tipos_documento_contable WHERE created_by = 'seed-f1-con-02-prueba';
