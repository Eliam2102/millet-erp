#!/usr/bin/env bash

set -euo pipefail

# Opcional, uso local: --shared-db (siempre primero) usa una base desechable en el
# Postgres de pruebas ya levantado (docker-compose.test.yml) en vez de crear un
# contenedor nuevo. Sin la bandera, el comportamiento no cambia.
# Opcional: MILLET_GATE_TIEMPOS_DIR guarda fases.tsv y resultados TRX en ese directorio.
# Sin la variable, la salida y los códigos de salida se conservan.
# Consultar: python3 tools/gate-tiempos.py <directorio> [--fallas].
shared_db=false
if [[ "${1:-}" == "--shared-db" ]]; then
  shared_db=true
  shift
fi

# CE-01a exige EntraId y ejecuta únicamente sus pruebas HTTP con JWT firmado.
ce_entra=false
af_mode=false
if [[ "${1:-}" == "--activos-fijos" ]]; then
  ce_entra=true
  af_mode=true
  shift
fi
if [[ "${1:-}" == "--comercio-exterior" ]]; then
  ce_entra=true
  shift
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
backend_dir="$repo_root/backend"
contexts_file="$script_dir/migration-contexts.txt"
audit_container="millet-integration-gate-$$"
audit_password="millet-integration-temporary"
audit_log_dir="$(mktemp -d "${TMPDIR:-/tmp}/millet-integration-gate.XXXXXX")"

local_dotnet="$repo_root/../.tools/dotnet/dotnet"
if [[ -x "$local_dotnet" ]] && (cd "$repo_root" && "$local_dotnet" --version >/dev/null 2>&1); then
  dotnet_bin="$local_dotnet"
else
  # La instalación local puede tener un SDK anterior al de global.json.
  # En ese caso usa el SDK compatible disponible en PATH.
  dotnet_bin="dotnet"
fi
(cd "$repo_root" && "$dotnet_bin" --version >/dev/null)

pg_container="$audit_container"
pg_user=pgadmin
pg_password="$audit_password"
pg_db=millet_dev
if [[ "$shared_db" == true ]]; then
  pg_container="${MILLET_GATE_SHARED_CONTAINER:-millet-erp-postgres-test-1}"
  pg_user="${MILLET_GATE_SHARED_USER:-postgres}"
  pg_password="${MILLET_GATE_SHARED_PASSWORD:-postgres}"
  pg_db="millet_gate_$$"
fi

tiempos_dir="${MILLET_GATE_TIEMPOS_DIR:-}"
tiempos_listos=false
fase_actual=""

iniciar_fase() {
  if [[ -n "$tiempos_dir" ]]; then
    fase_actual="$1"
    fase_inicio="$(date +%s)"
  fi
}

terminar_fase() {
  if [[ -n "$tiempos_dir" && -n "$fase_actual" ]]; then
    local resultado="${1:-0}"
    [[ "$resultado" != 0 ]] || resultado=ok
    printf '%s\t%s\t%s\n' "$fase_actual" "$(( $(date +%s) - fase_inicio ))" "$resultado" >> "$tiempos_dir/fases.tsv"
    fase_actual=""
  fi
}

medir_fase() {
  iniciar_fase "$1"
  shift
  "$@"
  terminar_fase
}

# Nombres internos del gate o leídos de pg_database; nunca recibe datos de la aplicación.
borrar_base_gate() {
  local base="${1//\"/\"\"}"
  docker exec "$pg_container" psql -U "$pg_user" -d postgres -c "ALTER DATABASE \"$base\" IS_TEMPLATE false" >/dev/null 2>&1 || true
  docker exec "$pg_container" psql -U "$pg_user" -d postgres -c "DROP DATABASE IF EXISTS \"$base\" WITH (FORCE)" >/dev/null 2>&1 || true
}

cleanup() {
  local codigo_salida=$?
  if [[ "$tiempos_listos" == true ]]; then
    terminar_fase "$codigo_salida"
    fase_actual=total
    fase_inicio="$tiempos_inicio"
    terminar_fase "$codigo_salida"
    tiempos_listos=false
  fi
  if [[ "$shared_db" == true ]]; then
    local base_ce
    for base_ce in $(docker exec "$pg_container" psql -U "$pg_user" -d postgres -At -c "SELECT datname FROM pg_database WHERE datname = 'ce_tpl_$pg_db' OR starts_with(datname, 'ce_integration_${pg_db}_')" 2>/dev/null); do
      borrar_base_gate "$base_ce"
    done
    borrar_base_gate "$pg_db"
  else
    docker stop "$audit_container" >/dev/null 2>&1 || true
  fi
  rm -r -- "$audit_log_dir" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

if [[ -n "$tiempos_dir" ]]; then
  # Ruta absoluta: más adelante el gate cambia al directorio backend.
  mkdir -p -- "$tiempos_dir"
  tiempos_dir="$(cd "$tiempos_dir" && pwd)"
  tiempos_inicio="$(date +%s)"
  printf 'fase\tsegundos\tresultado\n' > "$tiempos_dir/fases.tsv"
  tiempos_listos=true
fi
iniciar_fase contenedor_o_base

docker info >/dev/null
if [[ "$shared_db" == true ]]; then
  if [[ "$(docker inspect -f '{{.State.Running}}' "$pg_container" 2>/dev/null)" != true ]]; then
    echo "--shared-db: el contenedor $pg_container no está corriendo (docker compose -f docker-compose.test.yml up -d)." >&2
    exit 2
  fi
  # Bases de corridas que murieron sin limpiar (p. ej. kill -9): su proceso ya no existe.
  for stale in $(docker exec "$pg_container" psql -U "$pg_user" -d postgres -At -c "SELECT datname FROM pg_database WHERE datname ~ '^millet_gate_[0-9]+$'"); do
    kill -0 "${stale#millet_gate_}" 2>/dev/null && continue
    for clon in $(docker exec "$pg_container" psql -U "$pg_user" -d postgres -At -c "SELECT datname FROM pg_database WHERE starts_with(datname, 'ce_integration_${stale}_')" 2>/dev/null); do
      borrar_base_gate "$clon"
    done
    borrar_base_gate "ce_tpl_$stale"
    borrar_base_gate "$stale"
  done
  docker exec "$pg_container" psql -U "$pg_user" -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"$pg_db\"" >/dev/null
else
docker run --rm -d \
  --name "$audit_container" \
  -e POSTGRES_USER=pgadmin \
  -e POSTGRES_PASSWORD="$audit_password" \
  -e POSTGRES_DB=millet_dev \
  -p 127.0.0.1::5432 \
  postgres:17-alpine >/dev/null
fi

ready=0
for _ in {1..30}; do
  if docker exec "$pg_container" pg_isready -U "$pg_user" -d "$pg_db" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done

if [[ "$ready" -ne 1 ]]; then
  echo "PostgreSQL temporal no quedó listo." >&2
  exit 2
fi

port_mapping="$(docker port "$pg_container" 5432/tcp | head -n 1)"
audit_port="${port_mapping##*:}"

terminar_fase

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$audit_port;Database=$pg_db;Username=$pg_user;Password=$pg_password;Include Error Detail=true"
export ASPNETCORE_ENVIRONMENT="Development"
# El bootstrap que corre con las migraciones también lee user-secrets: sin
# esto, un tenant real guardado ahí siembra su OID como SuperAdmin y los
# tests (que entran como 'dev-superadmin') reciben 403.
if [[ "$ce_entra" == true ]]; then
  export Auth__Mode="EntraId"
  export MILLET_CE_ENTRA_TESTS="1"
  # Proveedor de altas de colaboradores (Graph), no del inicio de sesión.
  export Entra__Proveedor="Simulado"
else
export Auth__Mode="FakeForLocalDev"
export Auth__InitialAdminEntraOid="dev-superadmin"
export Entra__Proveedor="Simulado"
fi

projects=(
  'tests/Integraciones.Aw.IntegrationTests/Millet.Integraciones.Aw.IntegrationTests.csproj'
  'tests/Compras.IntegrationTests/Millet.Compras.IntegrationTests.csproj'
  'tests/Api.IntegrationTests/Millet.Api.IntegrationTests.csproj'
)

if [[ "$ce_entra" == true ]]; then
  projects=('tests/Api.IntegrationTests/Millet.Api.IntegrationTests.csproj')
  if [[ "$af_mode" == true ]]; then
    set -- --filter 'FullyQualifiedName~Millet.Api.IntegrationTests.ActivosFijos' "$@"
  else
    set -- --filter 'FullyQualifiedName~Millet.Api.IntegrationTests.ComercioExterior' "$@"
  fi
fi

# F1 (Parte F): argumentos extra (p. ej. --filter "FullyQualifiedName~Empleado")
# se reenvían tal cual a cada `dotnet test`. Con VSTest 17.x, un proyecto sin
# coincidencias para el filtro termina en 0 ("Ninguna prueba coincide con el
# filtro..."), así que no hace falta lógica especial para no tumbar el script.
cd "$backend_dir"
medir_fase tool_restore "$dotnet_bin" tool restore
medir_fase restore "$dotnet_bin" restore Millet.sln
medir_fase build "$dotnet_bin" build Millet.sln --configuration Debug --no-restore

while IFS= read -r item; do
  item="${item%$'\r'}"
  [[ -z "$item" || "$item" == \#* ]] && continue
  IFS='|' read -r context project <<< "$item"
  echo "Migrando $context"
  migration_log="$audit_log_dir/migrate-$context.log"
  iniciar_fase "migracion:$context"
  if "$dotnet_bin" ef database update \
    --context "$context" \
    --project "$project" \
    --startup-project src/Api/Millet.Api.csproj \
    --configuration Debug \
    --no-build >"$migration_log" 2>&1; then
    terminar_fase
  else
    terminar_fase "$?"
    tail -n 40 "$migration_log" >&2
    exit 3
  fi
done < "$contexts_file"

# Fixture de pruebas: fila general de umbral que reproduce el comportamiento previo (RQ basta con N1 salvo naturaleza o monto; OC exige N2 por monto > 0). Las pruebas del modo por departamento siembran su propia fila.
medir_fase fixture_sql docker exec "$pg_container" psql -U "$pg_user" -d "$pg_db" -v ON_ERROR_STOP=1 -c "INSERT INTO compras.umbrales_aprobacion_departamento (empresa_id, departamento_id, vigente_desde, vigente_hasta, umbral_menor_cuantia, umbral_monto, moneda, modo_nivel2, monto_minimo_disponibilidad) VALUES ('00000003-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000000', '2020-01-01', NULL, NULL, 0, 'MXN', 2, 999999999999.99) ON CONFLICT DO NOTHING;" >/dev/null

proyectos_fallidos=()
for project in "${projects[@]}"; do
  proyecto="$(basename "$project" .csproj)"
  echo "Probando $(basename "$(dirname "$project")")"
  test_args=(--configuration Debug --no-build --no-restore "$@")
  if [[ -n "$tiempos_dir" ]]; then
    test_args+=(--logger "trx;LogFileName=$proyecto.trx" --results-directory "$tiempos_dir")
  fi
  # Un proyecto en rojo no detiene a los demás: se ven todas las fallas en una corrida.
  iniciar_fase "test:$proyecto"
  if "$dotnet_bin" test "$project" "${test_args[@]}"; then
    terminar_fase
  else
    codigo=$?
    terminar_fase "$codigo"
    proyectos_fallidos+=("$proyecto")
  fi
done

if [[ "${#proyectos_fallidos[@]}" -gt 0 ]]; then
  echo "Gate de integración RECHAZADO: fallaron ${proyectos_fallidos[*]} (ver detalle arriba)." >&2
  exit 1
fi

echo "Gate de integración aprobado: todas las suites seleccionadas terminaron correctamente (ver conteos arriba)."
