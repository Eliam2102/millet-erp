#!/usr/bin/env bash
set -euo pipefail

# Ejecutar solo contra API Development + FakeForLocalDev y Mailpit en loopback.
# Crea datos ficticios en la BD local indicada al iniciar la API.
adm_api_url="${ADM_API_URL:-http://127.0.0.1:5005}"
adm_mailpit_url="${ADM_MAILPIT_URL:-http://127.0.0.1:8025}"
if [[ "$adm_api_url" != http://127.0.0.1:* && "$adm_api_url" != http://localhost:* ]]; then
  printf 'La prueba solo acepta una API HTTP en loopback.\n' >&2
  exit 2
fi
if [[ "$adm_mailpit_url" != http://127.0.0.1:* && "$adm_mailpit_url" != http://localhost:* ]]; then
  printf 'La prueba solo acepta Mailpit HTTP en loopback.\n' >&2
  exit 2
fi

adm_unique="$(uuidgen | tr '[:upper:]' '[:lower:]' | cut -c1-8)"
adm_empty_id='00000000-0000-0000-0000-000000000000'
adm_upn="qa-${adm_unique}@millet.mx"
adm_contact="qa-${adm_unique}@example.test"
adm_mail_before_ids="$(curl -fsS "$adm_mailpit_url/api/v1/messages" | jq -ce '[.messages[].ID]')"

adm_token="$(curl -fsS "$adm_api_url/api/dev/fake-login" \
  -H 'Content-Type: application/json' \
  -d '{"entraOid":"dev-superadmin","email":"superadmin@dev.local","nombre":"Super Admin Dev","empresaId":null}' \
  | jq -er '.accessToken')"

adm_get() {
  curl -fsS -H "Authorization: Bearer $adm_token" "$adm_api_url$1"
}
adm_post() {
  curl -fsS -X POST "$adm_api_url$1" \
    -H "Authorization: Bearer $adm_token" \
    -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $(uuidgen)" \
    -d "$2"
}

adm_role_id="$(adm_get '/api/v1/identidad/roles?limit=200' \
  | jq -er '.items[] | select(.codigo == "admin-identidad") | .id')"

adm_sucursal_id="$(adm_post '/api/v1/admin/empresas/sucursales' \
  "$(jq -n --arg id "$adm_empty_id" --arg key "QA-$adm_unique" \
    '{id:$id,clave:$key,nombre:("Sucursal " + $key)}')" | jq -er '.id')"
adm_departamento_id="$(adm_post '/api/v1/admin/departamentos' \
  "$(jq -n --arg id "$adm_empty_id" --arg key "QD-$adm_unique" \
    '{id:$id,clave:$key,nombre:("Departamento " + $key)}')" | jq -er '.id')"
adm_post "/api/v1/admin/empresas/sucursales/$adm_sucursal_id/departamentos/$adm_departamento_id" '{}' >/dev/null
adm_puesto_id="$(adm_post '/api/v1/admin/puestos' \
  "$(jq -n --arg id "$adm_empty_id" --arg key "QP-$adm_unique" --arg role "$adm_role_id" \
    '{id:$id,clave:$key,nombre:("Puesto " + $key),rolSugeridoId:$role}')" | jq -er '.id')"
adm_post "/api/v1/admin/empresas/sucursales/$adm_sucursal_id/puestos/$adm_puesto_id" \
  "$(jq -n --arg dept "$adm_departamento_id" '{departamentoId:$dept}')" >/dev/null

adm_alta="$(adm_post '/api/v1/admin/colaboradores' \
  "$(jq -n --arg id "$adm_empty_id" --arg key "QC-$adm_unique" \
    --arg suc "$adm_sucursal_id" --arg dept "$adm_departamento_id" \
    --arg puesto "$adm_puesto_id" --arg role "$adm_role_id" \
    --arg upn "$adm_upn" --arg contact "$adm_contact" \
    '{id:$id,clave:$key,nombre:("Colaborador " + $key),sucursalId:$suc,
      departamentoId:$dept,puestoId:$puesto,acceso:2,correoCorporativo:$upn,
      emailContacto:$contact,rolId:$role}')")"
adm_empleado_id="$(jq -er '.empleado.id' <<< "$adm_alta")"
adm_usuario_id="$(jq -er '.acceso.usuarioId' <<< "$adm_alta")"
test "$(jq -er '.acceso.estadoAcceso' <<< "$adm_alta")" = '2'

adm_estado='2'
for _ in {1..12}; do
  adm_estado="$(adm_get "/api/v1/admin/colaboradores/$adm_empleado_id/acceso" | jq -er '.estadoAcceso')"
  if [[ "$adm_estado" == '1' || "$adm_estado" == '3' ]]; then break; fi
  sleep 5
done
if [[ "$adm_estado" != '1' ]]; then
  printf 'La provisión no terminó correctamente. Estado=%s; revisa Mailpit y logs de API.\n' "$adm_estado" >&2
  exit 1
fi

adm_mailbox="$(curl -fsS "$adm_mailpit_url/api/v1/messages")"
adm_nuevo_id="$(jq -er --argjson anteriores "$adm_mail_before_ids" \
  --arg recipient "$adm_contact" \
  '[.messages[] | select((.ID as $id | $anteriores | index($id)) == null)
    | select(.Subject == "[PRUEBA LOCAL] Acceso al ERP Millet")
    | select(any(.To[]?; .Address == $recipient)) | .ID]
   | if length == 1 then .[0] else error("se esperaba exactamente un correo nuevo para el colaborador") end' \
  <<< "$adm_mailbox")"
adm_mensaje="$(curl -fsS "$adm_mailpit_url/api/v1/message/$adm_nuevo_id")"
if ! jq -e --arg upn "$adm_upn" --arg name "Colaborador QC-$adm_unique" \
  '.Text | contains($upn) and contains($name) and contains("Clave temporal simulada")' \
  <<< "$adm_mensaje" >/dev/null; then
  printf 'Mailpit capturó el correo, pero su contenido no corresponde al colaborador.\n' >&2
  exit 1
fi

adm_detalle="$(adm_get "/api/v1/identidad/usuarios/$adm_usuario_id")"
adm_oid="$(jq -er '.usuario.entraOid' <<< "$adm_detalle")"
test "$(jq -er --arg role "$adm_role_id" '[.asignaciones[] | select(.rolId == $role)] | length > 0' <<< "$adm_detalle")" = 'true'
test "$(adm_get "/api/v1/admin/empresas/sucursales/$adm_sucursal_id/usuarios" \
  | jq -er --arg user "$adm_usuario_id" '[.items[] | select(.usuarioId == $user)] | length > 0')" = 'true'
adm_login="$(curl -fsS "$adm_api_url/api/dev/fake-login" \
  -H 'Content-Type: application/json' \
  -d "$(jq -n --arg oid "$adm_oid" --arg upn "$adm_upn" \
    '{entraOid:$oid,email:$upn,nombre:"Colaborador QA",empresaId:null}')")"
test "$(jq -er '.usuario.id' <<< "$adm_login")" = "$adm_usuario_id"
test "$(jq -er '.permisos | length > 0' <<< "$adm_login")" = 'true'
test "$(adm_get "/api/v1/admin/colaboradores/$adm_empleado_id/acceso" | jq -er '.estadoAcceso')" = '0'

printf 'OK: alta, provisión, correo local y acceso simulado con usuario %s.\n' "$adm_upn"
printf 'Empleado=%s Cuenta=%s Sucursal=%s\n' "$adm_empleado_id" "$adm_usuario_id" "$adm_sucursal_id"
printf 'Correo de prueba: %s (Mailpit: %s). No se envió a Internet.\n' "$adm_contact" "$adm_mailpit_url"
