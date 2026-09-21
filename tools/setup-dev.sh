#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
backend_dir="$repo_root/backend"
frontend_dir="$repo_root/frontend"
contexts_file="$script_dir/migration-contexts.txt"

skip_migrations=0
skip_npm_install=0
skip_database_start=0
postgres_port="${POSTGRES_PORT:-5432}"

usage() {
  cat <<'EOF'
Uso: ./tools/setup-dev.sh [opciones]

Prepara Millet ERP en macOS, Linux o WSL/Git Bash.

Opciones:
  --postgres-port PUERTO  Puerto local de PostgreSQL (default: 5432)
  --skip-migrations       No aplica migraciones EF
  --skip-npm-install      No instala dependencias del frontend
  --skip-database-start   No ejecuta docker compose up
  -h, --help              Muestra esta ayuda
EOF
}

fail() {
  printf '\nERROR: %s\n\n' "$1" >&2
  exit 1
}

step() {
  printf '\n==> %s\n' "$1"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --postgres-port)
      [[ $# -ge 2 ]] || fail "Falta el valor de --postgres-port."
      postgres_port="$2"
      shift 2
      ;;
    --skip-migrations) skip_migrations=1; shift ;;
    --skip-npm-install) skip_npm_install=1; shift ;;
    --skip-database-start) skip_database_start=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) fail "Opción desconocida: $1" ;;
  esac
done

[[ "$postgres_port" =~ ^[0-9]+$ ]] || fail "El puerto debe ser numérico."
(( postgres_port >= 1 && postgres_port <= 65535 )) || fail "Puerto fuera de rango."
[[ -d "$repo_root/.git" ]] || fail "No se encontró .git en $repo_root."
[[ -f "$contexts_file" ]] || fail "No se encontró $contexts_file."

for command_name in dotnet node npm docker; do
  command -v "$command_name" >/dev/null 2>&1 || fail "Falta '$command_name' en PATH."
done
docker compose version >/dev/null 2>&1 || fail "Docker Compose no está disponible."

step "Validando versiones"
dotnet_sdks="$(dotnet --list-sdks)"
grep -qE '^9\.' <<<"$dotnet_sdks" || fail "Se requiere .NET SDK 9.x. Instalados: $dotnet_sdks"
node_version="$(node --version)"
[[ "$node_version" =~ ^v22\. ]] || fail "Se requiere Node.js 22.x; se encontró $node_version."
printf '    .NET 9 y Node %s detectados.\n' "$node_version"

export POSTGRES_PORT="$postgres_port"
export POSTGRES_VOLUME_NAME="${POSTGRES_VOLUME_NAME:-millet-dev-pg-data-$postgres_port}"
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$postgres_port;Database=millet_dev;Username=pgadmin;Password=pgadmin;Include Error Detail=true"

if [[ "$skip_database_start" -eq 0 ]]; then
  step "Levantando PostgreSQL en 127.0.0.1:$postgres_port"
  docker compose -f "$repo_root/docker-compose.dev.yml" up -d postgres
fi

step "Esperando PostgreSQL"
ready=0
for _ in {1..30}; do
  if docker compose -f "$repo_root/docker-compose.dev.yml" exec -T postgres \
      pg_isready -U pgadmin -d millet_dev >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done
[[ "$ready" -eq 1 ]] || fail "PostgreSQL no quedó listo en 30 segundos."

step "Restaurando y compilando backend"
cd "$backend_dir"
dotnet tool restore
dotnet restore Millet.sln
dotnet build Millet.sln --configuration Debug --no-restore --nologo

if [[ "$skip_migrations" -eq 0 ]]; then
  step "Aplicando migraciones EF"
  while IFS='|' read -r context project; do
    [[ -z "$context" || "$context" == \#* ]] && continue
    printf '    -> %s\n' "$context"
    dotnet ef database update \
      --context "$context" \
      --project "$project" \
      --startup-project src/Api/Millet.Api.csproj \
      --configuration Debug \
      --no-build
  done < "$contexts_file"
fi

if [[ "$skip_npm_install" -eq 0 ]]; then
  step "Instalando dependencias del frontend"
  cd "$frontend_dir"
  npm install --ignore-scripts --no-audit --no-fund
fi

cat <<EOF

==> Setup completo.

API:       cd backend && dotnet watch run --project src/Api/Millet.Api.csproj
Frontend:  cd frontend && npm run dev
URL:       http://localhost:5173
Postgres:  127.0.0.1:$postgres_port

Ejecuta ./tools/validate-local.sh antes de abrir un pull request.
EOF
