#!/usr/bin/env bash
# Restablece la base de la demo a su estado de ensayo para practicar la presentacion
# tantas veces como quieras. Tarda unos segundos.
#
# Uso:
#   tools/demo/restablecer-demo.sh            # restaura la base de practica sobre millet_demo
#   tools/demo/restablecer-demo.sh --guardar  # toma el estado ACTUAL como nueva base de practica
#   tools/demo/restablecer-demo.sh --migrar   # restaura y aplica migraciones del codigo (si tu rama es mas nueva)
#   tools/demo/restablecer-demo.sh --estado   # muestra conteos de la base y de la copia de practica
#
# La copia de practica NO va en GitHub (trae correos y OID del equipo):
#   por omision ~/.millet-demo/base-practica.dump  (cambia la ruta con MILLET_DEMO_BASE)
# Solo opera sobre bases cuyo nombre empieza con "millet_demo" (nunca sobre dev).
# Despues de restablecer: REINICIA la API (tools/demo/demo.sh api) y recarga el navegador;
# la API guarda permisos en memoria y conexiones abiertas a la base anterior.
set -euo pipefail

aqui="$(cd "$(dirname "$0")" && pwd)"
raiz="$(cd "$aqui/../.." && pwd)"
CONTENEDOR="${PG_CONTAINER:-millet-dev-postgres}"
DEMO_DB="${DEMO_DB:-millet_demo}"
BASE="${MILLET_DEMO_BASE:-$HOME/.millet-demo/base-practica.dump}"

falla() { echo "ERROR: $*" >&2; exit 1; }
psql_c() { docker exec -i "$CONTENEDOR" psql -v ON_ERROR_STOP=1 -q -U pgadmin "$@"; }

[[ "$DEMO_DB" == millet_demo* ]] || falla "solo se restablecen bases millet_demo* (recibido: $DEMO_DB)"
docker ps --format '{{.Names}}' | grep -qx "$CONTENEDOR" \
  || falla "no corre el contenedor $CONTENEDOR (abre Docker y corre tools/demo/demo.sh preparar)"

conteos() {
  psql_c -d "$1" -At -F' ' -c "
    select 'usuarios', count(*) from identidad.usuarios union all
    select 'requisiciones', count(*) from compras.requisiciones union all
    select 'ordenes', count(*) from compras.ordenes_compra union all
    select 'recepciones', count(*) from almacen.movimientos_inventario union all
    select 'facturas', count(*) from cuentas_por_pagar.facturas_proveedor union all
    select 'proveedores', count(*) from compartido.proveedores union all
    select 'articulos', count(*) from compartido.articulos union all
    select 'clientes', count(*) from compartido.clientes" 2>/dev/null | tr '\n' ';' | sed 's/;/  /g'
}

case "${1:-}" in
  --guardar)
    mkdir -p "$(dirname "$BASE")"
    [[ -f "$BASE" ]] && cp "$BASE" "$BASE.anterior"
    docker exec "$CONTENEDOR" pg_dump -U pgadmin -d "$DEMO_DB" --no-owner --no-privileges -Fc > "$BASE"
    echo "Base de practica guardada desde $DEMO_DB: $BASE ($(du -h "$BASE" | cut -f1))"
    [[ -f "$BASE.anterior" ]] && echo "(la anterior quedo en $BASE.anterior)"
    exit 0 ;;
  --estado)
    echo "Base actual  ($DEMO_DB): $(conteos "$DEMO_DB")"
    [[ -f "$BASE" ]] && echo "Copia practica: $BASE ($(du -h "$BASE" | cut -f1), $(date -r "$BASE" '+%d/%m/%Y %H:%M'))" || echo "Copia practica: NO existe ($BASE)"
    exit 0 ;;
  ""|--migrar) ;;
  *) sed -n '2,16p' "$0"; exit 2 ;;
esac

[[ -f "$BASE" ]] || falla "no existe la copia de practica: $BASE
  Copia ahi el archivo base-practica.dump (o usa --guardar con una base que ya este como quieres)."

inicio=$SECONDS
echo "Restableciendo $DEMO_DB desde $BASE ..."
echo "  antes:   $(conteos "$DEMO_DB" || echo '(sin base)')"
psql_c -d postgres -c "DROP DATABASE IF EXISTS \"$DEMO_DB\" WITH (FORCE)"
psql_c -d postgres -c "CREATE DATABASE \"$DEMO_DB\""
docker exec -i "$CONTENEDOR" pg_restore -U pgadmin -d "$DEMO_DB" --no-owner --no-privileges < "$BASE" \
  || falla "fallo la restauracion"

if [[ "${1:-}" == "--migrar" ]]; then
  echo "Aplicando migraciones del codigo ..."
  cd "$raiz/backend"
  dotnet tool restore >/dev/null
  export ConnectionStrings__Postgres="Host=localhost;Port=${PG_PORT:-5432};Database=$DEMO_DB;Username=pgadmin;Password=pgadmin" ASPNETCORE_ENVIRONMENT=Development
  while IFS='|' read -r contexto proyecto; do
    contexto="${contexto%$'\r'}"; [[ -z "$contexto" || "$contexto" == \#* ]] && continue
    dotnet ef database update --context "$contexto" --project "$proyecto" \
      --startup-project src/Api/Millet.Api.csproj </dev/null >/tmp/millet-demo-migracion.log 2>&1 \
      || { tail -n 20 /tmp/millet-demo-migracion.log >&2; falla "no se pudo migrar $contexto"; }
    echo "  ok $contexto"
  done < "$raiz/tools/migration-contexts.txt"
fi

echo "  despues: $(conteos "$DEMO_DB")"
echo
"$aqui/verificar-usuarios-demo.sh" | tail -2
echo
echo "Listo en $((SECONDS - inicio)) s. Ahora: reinicia la API (tools/demo/demo.sh api) y recarga el navegador."
