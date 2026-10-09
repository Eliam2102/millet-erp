#!/bin/bash
# BD de prueba del origen de demo de A+W (tablas aw_origen.*) en el PostgreSQL de desarrollo.
# Uso: aw-origen-demo.sh {up|reseed|env|psql}
#  up      crea la BD si no existe y aplica schema.sql + seed.sql (idempotente)
#  reseed  re-aplica seed.sql (deshace los cambios hechos a mano a las tablas)
#  env     imprime las variables de entorno que apuntan la API a este origen (eval "$(... env)")
#  psql    abre psql sobre la BD
# Cambiar un dato a mano para ver la sincronización (queda en "Datos de origen A+W"; la razón social local no se pisa):
#   ./aw-origen-demo.sh psql -c "UPDATE aw_origen.ku_kunden SET name1='CLIENTE EDITADO' WHERE id=10"
set -euo pipefail
cd "$(dirname "$0")"
C="${PG_CONTAINER:-millet-dev-postgres}"; U="${PG_USER:-pgadmin}"; P="${PG_PASSWORD:-pgadmin}"; DB="${AW_ORIGEN_DB:-millet_aw_origen}"
pg() { docker exec -i -e PGOPTIONS='-c client_min_messages=warning' "$C" psql -v ON_ERROR_STOP=1 -q -U "$U" "$@"; }

case "${1:-}" in
  up)
    pg -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DB'" | grep -q 1 || pg -d postgres -c "CREATE DATABASE $DB"
    pg -d "$DB" < schema.sql
    pg -d "$DB" < seed.sql
    echo "origen demo listo: BD $DB en el contenedor $C (60 clientes, 47 productos)" ;;
  reseed) pg -d "$DB" < seed.sql ;;
  env)
    cat <<ENV
export ConnectionStrings__AwOrigenPgDb='Host=localhost;Port=5432;Database=$DB;Username=$U;Password=$P'
export IntegracionesAw__Clientes__Origen=Postgres
export IntegracionesAw__Clientes__LecturaHabilitada=true
export IntegracionesAw__Clientes__AplicacionHabilitada=true
export IntegracionesAw__Clientes__MapeoMoneda__PESOSMX=MXN
export IntegracionesAw__Clientes__MapeoMoneda__USD=USD
export IntegracionesAw__Clientes__MapeoMoneda__Euro=EUR
export IntegracionesAw__Productos__OrigenHabilitado=true
export IntegracionesAw__Productos__Origen=Postgres
ENV
    ;;
  psql) shift; docker exec -i $([ -t 0 ] && echo -t) "$C" psql -U "$U" -d "$DB" "$@" ;;
  *) sed -n 2,11p "$0"; exit 2 ;;
esac
