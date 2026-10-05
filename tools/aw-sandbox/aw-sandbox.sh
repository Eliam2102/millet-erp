#!/bin/bash
# Sandbox A+W local. Uso: aw-sandbox.sh {up|down|reset|mutar <caso>|reseed}
#  up      levanta, espera healthcheck, aplica schema + seed (idempotente)
#  down    para el contenedor (conserva volumen)
#  reset   borra volumen y hace up
#  reseed  re-aplica el seed (deshace mutaciones)
#  mutar   aplica mutaciones/<caso>.sql
set -euo pipefail
cd "$(dirname "$0")"
[ -f .env ] || { echo "falta .env (copia .env.example)" >&2; exit 1; }
set -a; . ./.env; set +a
: "${AW_SANDBOX_PASSWORD:?}" "${AW_SANDBOX_RO_PASSWORD:?}"
C=aw-sandbox
sq() { docker exec -i -e SQLCMDPASSWORD="$AW_SANDBOX_PASSWORD" "$C" /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa "$@"; }
aplicar() { for f in "$@"; do echo "-> $f"; sq -v RO_PASSWORD="$AW_SANDBOX_RO_PASSWORD" -d AW_SANDBOX -i "/aw/$f"; done; }

case "${1:-}" in
  up)
    docker compose up -d
    for _ in $(seq 60); do
      [ "$(docker inspect -f '{{.State.Health.Status}}' "$C" 2>/dev/null)" = healthy ] && break; sleep 3
    done
    [ "$(docker inspect -f '{{.State.Health.Status}}' "$C")" = healthy ] || { echo "no llegó a healthy" >&2; exit 1; }
    # El esquema solo se crea si no existe (01 no es idempotente); el seed sí.
    sq -d master -i /aw/schema/00-bd.sql
    if [ "$(sq -h -1 -W -d AW_SANDBOX -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('SYSADM')" | tr -d '[:space:]')" = 0 ]; then
      aplicar schema/01-esquema.sql
    fi
    aplicar schema/02-usuario-ro.sql
    aplicar seed/*.sql
    echo "sandbox listo en localhost,${AW_SANDBOX_PORT:-14330} (BD AW_SANDBOX; lector: aw_ro)" ;;
  reseed)
    for f in seed/*.sql; do sq -d AW_SANDBOX -i "/aw/$f"; done ;;
  down) docker compose down ;;
  reset) docker compose down -v; "$0" up ;;
  mutar)
    f="mutaciones/${2:?caso}.sql"; [ -f "$f" ] || { echo "caso inexistente; opciones: $(ls mutaciones | sed 's/\.sql//' | tr '\n' ' ')" >&2; exit 2; }
    sq -d AW_SANDBOX -i "/aw/$f"; echo "mutación '$2' aplicada" ;;
  *) echo "uso: $0 {up|down|reset|reseed|mutar <caso>}" >&2; exit 2 ;;
esac
