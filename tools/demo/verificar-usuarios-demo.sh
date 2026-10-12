#!/usr/bin/env bash
# Verifica que los usuarios, roles, sucursales y permisos de la demo coinciden con
# docs/operacion/demo-usuarios-permisos.csv (el estado con el que se ensayo la demo).
#
# Uso:
#   tools/demo/verificar-usuarios-demo.sh              # compara tu base contra el CSV
#   tools/demo/verificar-usuarios-demo.sh --exportar   # reescribe el CSV desde tu base
#
# Cada linea del CSV es  correo|tipo|valor  con tipo = rol | sucursal | permiso.
# Falta alguna linea (usuario sin rol, sin sucursal o sin permiso)  -> ERROR, sale 1.
# Sobra alguna (p. ej. tu propia cuenta de administrador)           -> AVISO, no falla.
# Requiere el contenedor de PostgreSQL de la demo (tools/demo/demo.sh preparar).
set -euo pipefail

aqui="$(cd "$(dirname "$0")" && pwd)"
raiz="$(cd "$aqui/../.." && pwd)"
csv="$raiz/docs/operacion/demo-usuarios-permisos.csv"
CONTENEDOR="${PG_CONTAINER:-millet-dev-postgres}"
DEMO_DB="${DEMO_DB:-millet_demo}"

docker ps --format '{{.Names}}' | grep -qx "$CONTENEDOR" \
  || { echo "ERROR: no corre el contenedor $CONTENEDOR (tools/demo/demo.sh preparar)" >&2; exit 2; }

consulta() {
  docker exec -i "$CONTENEDOR" psql -U pgadmin -d "$DEMO_DB" -At <<'SQL' | sort -u
with asignado as (
  select u.id usuario_id, lower(u.email) correo, uer.rol_id, r.codigo rol
  from identidad.usuarios u
  join identidad.usuario_empresa_roles uer on uer.usuario_id = u.id and uer.deleted_at is null
  join identidad.roles r on r.id = uer.rol_id
  where u.deleted_at is null
)
select correo || '|rol|' || rol from asignado
union all
select a.correo || '|sucursal|' || s.clave
from asignado a
join identidad.usuario_sucursales us on us.usuario_id = a.usuario_id and us.deleted_at is null and us.estatus = 0
join compartido.sucursales s on s.id = us.sucursal_id
union all
select a.correo || '|permiso|' || p.codigo
from asignado a
join identidad.rol_permisos rp on rp.rol_id = a.rol_id and rp.deleted_at is null
join identidad.permisos p on p.id = rp.permiso_id;
SQL
}

if [[ "${1:-}" == "--exportar" ]]; then
  consulta > "$csv"
  echo "CSV reescrito: $csv ($(wc -l < "$csv") lineas)"
  exit 0
fi

[[ -f "$csv" ]] || { echo "ERROR: falta $csv" >&2; exit 2; }
actual="$(mktemp)"; esperado="$(mktemp)"; trap 'rm -f "$actual" "$esperado"' EXIT
consulta > "$actual"; sort -u "$csv" > "$esperado"

faltan="$(comm -13 "$actual" "$esperado")"
sobran="$(comm -23 "$actual" "$esperado")"

echo "Base: $DEMO_DB   Esperado: $(wc -l < "$esperado") lineas   Encontrado: $(wc -l < "$actual") lineas"
echo
echo "Por usuario (rol · sucursales · permisos):"
while IFS= read -r correo; do
  rol="$(grep -c "^$correo|rol|" "$actual" || true)"
  suc="$(grep -c "^$correo|sucursal|" "$actual" || true)"
  per="$(grep -c "^$correo|permiso|" "$actual" || true)"
  esp="$(grep -c "^$correo|permiso|" "$esperado" || true)"
  rolnom="$(grep "^$correo|rol|" "$actual" | head -1 | cut -d'|' -f3)"
  marca="ok"; [[ "$per" -lt "$esp" ]] && marca="FALTAN $((esp - per)) permisos"
  printf '  %-52s %-26s suc=%s  permisos=%s/%s  %s\n' "$correo" "${rolnom:-SIN ROL}" "$suc" "$per" "$esp" "$marca"
done < <(cut -d'|' -f1 "$esperado" | sort -u)
echo

if [[ -n "$sobran" ]]; then
  echo "AVISO: en tu base hay $(echo "$sobran" | wc -l) lineas que no estan en el CSV (p. ej. tu cuenta o permisos nuevos):"
  echo "$sobran" | cut -d'|' -f1 | sort | uniq -c | sed 's/^/   /'
  echo
fi
if [[ -n "$faltan" ]]; then
  echo "ERROR: faltan $(echo "$faltan" | wc -l) lineas respecto del CSV:"
  echo "$faltan" | sed 's/^/   /' | head -60
  echo "   (restaura el respaldo: docs/operacion/demo-12oct-replicar-en-laptop.md, seccion 5)"
  exit 1
fi
echo "OK: todos los usuarios, roles, sucursales y permisos del CSV existen en tu base."
