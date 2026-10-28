#!/bin/bash
# BD de prueba del origen de demo de A+W (tablas aw_origen.*) en el PostgreSQL de desarrollo.
# Con PGHOST definido (más PGPORT/PGUSER/PGPASSWORD/PGSSLMODE, por omisión require) usa un PostgreSQL remoto en vez del contenedor.
# Uso: aw-origen-demo.sh {up|reseed|importar|pedidos|otr|piezas|env|psql}
#  up       crea la BD si no existe y aplica schema.sql + seed.sql + pedidos.sql + otr.sql + piezas.sql (idempotente)
#  reseed   re-aplica seed.sql (deshace los cambios hechos a mano a las tablas)
#  importar reemplaza clientes y productos por los de una BD de A+W en SQL Server (por defecto AW_DEMO del
#           sandbox tools/aw-sandbox) y re-siembra los pedidos; después la demo ya no necesita el sandbox
#  pedidos  re-siembra la cola de pedidos para Facturación (solo contra una BD del ERP nueva)
#  piezas   carga los escaneos de pieza terminada de muestra (flujo PT-01, idempotente)
#  env      imprime las variables de entorno que apuntan la API a este origen (eval "$(... env)")
#  psql     abre psql sobre la BD
# Cambiar un dato a mano para ver la sincronización (queda en "Datos de origen A+W"; la razón social local no se pisa):
#   ./aw-origen-demo.sh psql -c "UPDATE aw_origen.ku_kunden SET name1='CLIENTE EDITADO' WHERE id=10"
set -euo pipefail
cd "$(dirname "$0")"
C="${PG_CONTAINER:-millet-dev-postgres}"; PORT="${PG_PORT:-5432}"; U="${PG_USER:-pgadmin}"; P="${PG_PASSWORD:-pgadmin}"; DB="${AW_ORIGEN_DB:-millet_aw_origen}"
# Modo remoto: con PGHOST definido corre psql directo (PGPORT/PGUSER/PGPASSWORD del entorno, PGSSLMODE por omisión require).
if [ -n "${PGHOST:-}" ]; then
  export PGSSLMODE="${PGSSLMODE:-require}"; export PGPASSWORD="${PGPASSWORD:-$P}"
  PORT="${PGPORT:-5432}"; U="${PGUSER:-$U}"; P="$PGPASSWORD"; C="$PGHOST"
  pg() { PGOPTIONS='-c client_min_messages=warning' psql -h "$PGHOST" -p "$PORT" -v ON_ERROR_STOP=1 -q -U "$U" "$@"; }
else
pg() { docker exec -i -e PGOPTIONS='-c client_min_messages=warning' "$C" psql -v ON_ERROR_STOP=1 -q -U "$U" "$@"; }
fi

case "${1:-}" in
  up)
    pg -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DB'" | grep -q 1 || pg -d postgres -c "CREATE DATABASE $DB"
    pg -d "$DB" < schema.sql
    pg -d "$DB" < seed.sql
    pg -d "$DB" < pedidos.sql
    pg -d "$DB" -v sucursal_clave_aw="${OTR_SUCURSAL_CLAVE_AW:-MID}" \
      -v cliente_numero_aw="${OTR_CLIENTE_NUMERO_AW:-DEMO-CLI-EXP-1}" < otr.sql
    pg -d "$DB" < piezas.sql
    echo "origen demo listo: BD $DB en $C (62 clientes, 47 productos, 3 pedidos, 2 envíos OTR/3 versiones, 8 escaneos de pieza PT)" ;;
  reseed) pg -d "$DB" < seed.sql ;;
  importar)
    AWDB="${AW_DB:-AW_DEMO}"
    RO="$(grep '^AW_SANDBOX_RO_PASSWORD=' ../aw-sandbox/.env | cut -d= -f2-)"
    dotnet run importar-desde-aw.cs -- \
      "${AW_SQL_CS:-Server=localhost,14330;Database=$AWDB;User Id=aw_ro;Password=$RO;Encrypt=True;TrustServerCertificate=True}" \
      "Host=${PGHOST:-localhost};Port=$PORT;Database=$DB;Username=$U;Password=$P" 2>&1 | grep -v ': warning '
    pg -d "$DB" < pedidos.sql ;;
  pedidos) pg -d "$DB" < pedidos.sql ;;
  otr)
    pg -d "$DB" -v sucursal_clave_aw="${OTR_SUCURSAL_CLAVE_AW:-MID}" \
      -v cliente_numero_aw="${OTR_CLIENTE_NUMERO_AW:-DEMO-CLI-EXP-1}" < otr.sql ;;
  piezas) pg -d "$DB" < piezas.sql ;;
  env)
    cat <<ENV
export ConnectionStrings__AwOrigenPgDb='Host=${PGHOST:-localhost};Port=$PORT;Database=$DB;Username=$U;Password=$P'
export IntegracionesAw__OrigenDemo__Permitido=true
ENV
    ;;
  psql) shift; [ -n "${PGHOST:-}" ] && exec psql -h "$PGHOST" -p "$PORT" -U "$U" -d "$DB" "$@"; docker exec -i $([ -t 0 ] && echo -t) "$C" psql -U "$U" -d "$DB" "$@" ;;
  *) sed -n 2,14p "$0"; exit 2 ;;
esac
