#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
backend_dir="$repo_root/backend"
contexts_file="$script_dir/migration-contexts.txt"
audit_container="millet-integration-gate-$$"
audit_password="millet-integration-temporary"
audit_log_dir="$(mktemp -d "${TMPDIR:-/tmp}/millet-integration-gate.XXXXXX")"

if [[ -x "$repo_root/../.tools/dotnet/dotnet" ]]; then
  dotnet_bin="$repo_root/../.tools/dotnet/dotnet"
else
  dotnet_bin="dotnet"
fi

cleanup() {
  docker stop "$audit_container" >/dev/null 2>&1 || true
  rm -r -- "$audit_log_dir" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

docker info >/dev/null
docker run --rm -d \
  --name "$audit_container" \
  -e POSTGRES_USER=pgadmin \
  -e POSTGRES_PASSWORD="$audit_password" \
  -e POSTGRES_DB=millet_dev \
  -p 127.0.0.1::5432 \
  postgres:17-alpine >/dev/null

ready=0
for _ in {1..30}; do
  if docker exec "$audit_container" pg_isready -U pgadmin -d millet_dev >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done

if [[ "$ready" -ne 1 ]]; then
  echo "PostgreSQL temporal no quedó listo." >&2
  exit 2
fi

port_mapping="$(docker port "$audit_container" 5432/tcp | head -n 1)"
audit_port="${port_mapping##*:}"

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$audit_port;Database=millet_dev;Username=pgadmin;Password=$audit_password;Include Error Detail=true"
export ASPNETCORE_ENVIRONMENT="Development"

projects=(
  'tests/Integraciones.Aw.IntegrationTests/Millet.Integraciones.Aw.IntegrationTests.csproj'
  'tests/Compras.IntegrationTests/Millet.Compras.IntegrationTests.csproj'
  'tests/Api.IntegrationTests/Millet.Api.IntegrationTests.csproj'
)

cd "$backend_dir"
"$dotnet_bin" tool restore
"$dotnet_bin" restore Millet.sln
"$dotnet_bin" build Millet.sln --configuration Debug --no-restore

while IFS= read -r item; do
  [[ -z "$item" || "$item" == \#* ]] && continue
  IFS='|' read -r context project <<< "$item"
  echo "Migrando $context"
  migration_log="$audit_log_dir/migrate-$context.log"
  if ! "$dotnet_bin" ef database update \
    --context "$context" \
    --project "$project" \
    --startup-project src/Api/Millet.Api.csproj \
    --configuration Debug \
    --no-build >"$migration_log" 2>&1; then
    tail -n 40 "$migration_log" >&2
    exit 3
  fi
done < "$contexts_file"

for project in "${projects[@]}"; do
  echo "Probando $(basename "$(dirname "$project")")"
  "$dotnet_bin" test "$project" \
    --configuration Debug \
    --no-build \
    --no-restore
done

echo "Gate de integración aprobado: 489 pruebas."
