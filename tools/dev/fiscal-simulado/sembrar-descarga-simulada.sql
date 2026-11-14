-- M-293b · Siembra de DESARROLLO para probar la descarga masiva con el SDK fiscal simulado.
-- NO sube FIEL, CSD ni llaves reales: la api key es de relleno y las fechas de FIEL son ficticias.
-- Requiere la API en Development con  IntegracionesFiscal:Sdk:Modo=Simulado  (fuera de Development no arranca).
--
-- Uso (psql), con la empresa de la base de pruebas y un RFC de pruebas:
--   psql -d <base_de_desarrollo> -v empresa_id="'<uuid-de-la-empresa>'" -v rfc="'ABC010101AB1'" \
--        -f tools/dev/fiscal-simulado/sembrar-descarga-simulada.sql
-- Para arrancar los workers en desarrollo:
--   IntegracionesFiscal__Workers__Submitter__Disabled=false  IntegracionesFiscal__Workers__Poller__Disabled=false
--   (y, si hace falta, un TickIntervalSeconds corto y BackfillDays=1)

BEGIN;

INSERT INTO integraciones_fiscal.configuracion_pac
    (id, empresa_id, proveedor, base_url, api_key_cifrado, api_key_hash, activo, version, created_at, updated_at)
VALUES
    (gen_random_uuid(), :empresa_id::uuid, 1, 'https://live.fiscalapi.com', '\x00'::bytea, repeat('0', 64), true, 1, now(), now())
ON CONFLICT (empresa_id, proveedor) DO UPDATE SET activo = true, updated_at = now();

INSERT INTO integraciones_fiscal.rfcs_receptores
    (id, empresa_id, rfc, descarga_habilitada, refresh_habilitada, person_id_externo,
     cer_file_id_externo, key_file_id_externo, fiel_valid_from, fiel_valid_to, fiel_subida_at, version, created_at, updated_at)
VALUES
    (gen_random_uuid(), :empresa_id::uuid, upper(:rfc), true, false, 'SIM-PERSON-' || upper(:rfc),
     'SIM-CER', 'SIM-KEY', now() - interval '1 day', now() + interval '1 year', now(), 1, now(), now())
ON CONFLICT (empresa_id, rfc) DO UPDATE
    SET descarga_habilitada = true, person_id_externo = EXCLUDED.person_id_externo,
        fiel_valid_from = EXCLUDED.fiel_valid_from, fiel_valid_to = EXCLUDED.fiel_valid_to, updated_at = now();

COMMIT;
