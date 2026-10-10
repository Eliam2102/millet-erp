#!/usr/bin/env bash
# Demo local del ERP Millet en cualquier Mac (Apple Silicon o Intel).
# Uso: tools/demo/demo.sh {preparar [paquete.tar.gz] | eventos | api | web | estado}
#
#  preparar   PostgreSQL en Docker, base de la demo, migraciones, copia de A+W de demo
#             y dependencias del frontend. Con la ruta del paquete de datos de Geovany
#             (paquete-demo-real.tar.gz) también carga esos clientes, productos y proveedores.
#  eventos    Levanta el emulador de Service Bus (para que los módulos se pasen eventos).
#  api        Arranca la API en http://localhost:5080 (deja esta terminal abierta).
#  web        Arranca la pantalla en http://localhost:5173 (otra terminal).
#  estado     Revisa que todo responda.
#
# La configuración privada vive en tools/demo/.env.demo.local (ver .env.demo.example).
# Solo se entra con cuenta de Microsoft: no existe acceso de prueba.
set -euo pipefail

aqui="$(cd "$(dirname "$0")" && pwd)"
raiz="$(cd "$aqui/../.." && pwd)"
envf="$aqui/.env.demo.local"

falla() { echo "ERROR: $*" >&2; exit 1; }
paso() { echo; echo "==> $*"; }

[[ -f "$envf" ]] || falla "falta $envf. Copia tools/demo/.env.demo.example y llena los valores."
set -a; source "$envf"; set +a

PG_PORT="${PG_PORT:-5432}"; DEMO_DB="${DEMO_DB:-millet_demo}"
# Sin configuración completa de Graph, el alta de colaboradores se simula (no crea cuentas en Microsoft).
export Entra__Proveedor="${Entra__Proveedor:-Simulado}"
CONTENEDOR=millet-dev-postgres
export PG_PORT PG_CONTAINER="$CONTENEDOR"
CADENA="Host=localhost;Port=$PG_PORT;Database=$DEMO_DB;Username=pgadmin;Password=pgadmin"

psql_c() { docker exec -i "$CONTENEDOR" psql -v ON_ERROR_STOP=1 -q -U pgadmin "$@"; }

preparar() {
  paso "Revisando herramientas"
  for c in docker dotnet node npm python3; do command -v "$c" >/dev/null || falla "falta $c"; done
  dotnet --list-sdks | grep -qE '^10\.' || falla "se requiere .NET SDK 10"
  docker info >/dev/null 2>&1 || falla "Docker no está corriendo (abre OrbStack o Docker Desktop)"

  paso "PostgreSQL en Docker (puerto $PG_PORT)"
  if docker ps --format '{{.Names}}' | grep -qx "$CONTENEDOR"; then
    echo "   $CONTENEDOR ya está corriendo"
  else
    POSTGRES_PORT="$PG_PORT" docker compose -f "$raiz/docker-compose.dev.yml" up -d postgres
  fi
  for _ in $(seq 60); do docker exec "$CONTENEDOR" pg_isready -U pgadmin >/dev/null 2>&1 && break; sleep 2; done
  psql_c -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DEMO_DB'" | grep -q 1 \
    || psql_c -d postgres -c "CREATE DATABASE $DEMO_DB"

  paso "Compilando y aplicando migraciones a $DEMO_DB"
  cd "$raiz/backend"
  dotnet tool restore >/dev/null
  dotnet build Millet.sln -v q --nologo
  export ConnectionStrings__Postgres="$CADENA" ASPNETCORE_ENVIRONMENT=Development
  while IFS='|' read -r contexto proyecto; do
    contexto="${contexto%$'\r'}"; [[ -z "$contexto" || "$contexto" == \#* ]] && continue
    echo "   $contexto"
    dotnet ef database update --context "$contexto" --project "$proyecto" \
      --startup-project src/Api/Millet.Api.csproj --no-build </dev/null >/tmp/millet-demo-migracion.log 2>&1 \
      || { tail -n 30 /tmp/millet-demo-migracion.log >&2; falla "no se pudo migrar $contexto"; }
  done < "$raiz/tools/migration-contexts.txt"

  paso "Copia de A+W para la demo"
  "$raiz/tools/aw-origen-demo/aw-origen-demo.sh" up
  if [[ -n "${1:-}" ]]; then
    [[ -f "$1" ]] || falla "no existe el paquete $1"
    tmp="$(mktemp -d)"; tar -xzf "$1" -C "$tmp"
    [[ -f "$tmp/01_aw_origen.sql" && -f "$tmp/02_sap_maestros.sql" ]] || falla "el paquete no trae 01_aw_origen.sql y 02_sap_maestros.sql"
    paso "Cargando el paquete de datos (clientes, productos y proveedores)"
    psql_c -d "${AW_ORIGEN_DB:-millet_aw_origen}" < "$tmp/01_aw_origen.sql"
    "$raiz/tools/aw-origen-demo/aw-origen-demo.sh" pedidos
    psql_c -d "$DEMO_DB" < "$tmp/02_sap_maestros.sql"
    rm -rf "$tmp"
    echo "   Listo. Borra el paquete de tu computadora cuando ya no lo necesites."
  fi

  paso "Dependencias del frontend"
  cd "$raiz/frontend"
  [[ -d node_modules ]] || npm install --no-audit --no-fund
  {
    for v in VITE_AUTH_MODE VITE_API_BASE_URL VITE_ENTRA_TENANT_ID VITE_ENTRA_CLIENT_ID VITE_ENTRA_DOMAIN VITE_API_AUDIENCE; do
      echo "$v=${!v:-}"
    done
  } > .env.development.local
  echo
  echo "Preparado. Siguiente: tools/demo/demo.sh eventos, luego api y web en terminales separadas."
}

eventos() { "$raiz/tools/servicebus-emulator/levantar.sh"; }

api() {
  export ASPNETCORE_ENVIRONMENT=Development
  export ConnectionStrings__Postgres="$CADENA"
  export Seed__DemoSesion__Habilitado=true Seed__DatosDemo__Habilitado=false
  export ServiceBus__Modo=EmuladorLocal
  export ConnectionStrings__AwOrigenPgDb="Host=localhost;Port=$PG_PORT;Database=${AW_ORIGEN_DB:-millet_aw_origen};Username=pgadmin;Password=pgadmin"
  export IntegracionesAw__OrigenDemo__Permitido=true
  export Cors__AllowedOrigins__0=http://localhost:5173
  cd "$raiz/backend"
  exec dotnet run --project src/Api/Millet.Api.csproj --no-launch-profile --urls http://localhost:5080
}

web() { cd "$raiz/frontend"; exec npm run dev -- --port 5173 --strictPort; }

estado() {
  printf 'PostgreSQL: '; docker exec "$CONTENEDOR" pg_isready -U pgadmin >/dev/null 2>&1 && echo ok || echo "no responde"
  printf 'Service Bus: '; (nc -z localhost 5672 >/dev/null 2>&1 && echo ok) || echo "no responde (tools/demo/demo.sh eventos)"
  printf 'API: '; curl -fsS -o /dev/null http://localhost:5080/health/ready && echo ok || echo "no responde (tools/demo/demo.sh api)"
  printf 'Pantalla: '; curl -fsS -o /dev/null http://localhost:5173 && echo ok || echo "no responde (tools/demo/demo.sh web)"
}

case "${1:-}" in
  preparar) shift; preparar "${1:-}" ;;
  eventos) eventos ;;
  api) api ;;
  web) web ;;
  estado) estado ;;
  *) sed -n '2,15p' "$0"; exit 2 ;;
esac
