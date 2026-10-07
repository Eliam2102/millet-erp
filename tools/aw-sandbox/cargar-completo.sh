#!/bin/bash
# Copia la A+W real (MILMAIN, solo SELECT vía bcp queryout) a la BD local AW_FULL del contenedor aw-sandbox. Requiere VPN.
# Datos de los últimos AW_ANIOS años (def. 2): cotizaciones/pedidos/notas de crédito por la fecha de su cabecera (DATUM_ERF) y sus tablas hijas por ID;
# logs grandes por su fecha; todo lo demás (maestros, catálogos) completo. Los datos quedan SOLO en el volumen Docker (nunca en el repo).
# Uso: cargar-completo.sh [esquema|datos|todo]   (reanudable: salta lo ya cargado, ver .cargado)  · PAR=3 tablas en paralelo
set -euo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a
AW_ENV=${AW_ENV:-$HOME/.config/millet/aw-ro.env}
g() { sed -n "s/^$1=//p" "$AW_ENV" | head -n1; }
export RH=$(g AW_HOST) RD=$(g AW_DB) RU=$(g AW_USER) RP=$(g AW_PASS) SP=$AW_SANDBOX_PASSWORD
export DESDE=${AW_DESDE:-$(date -d "${AW_ANIOS:-2} years ago" +%F)}
C=aw-sandbox BCP=/opt/mssql-tools18/bin/bcp
sq() { docker exec -i -e SQLCMDPASSWORD="$SP" $C /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa "$@"; }

esquema() {
  sq -d master -Q "IF DB_ID('AW_FULL') IS NULL CREATE DATABASE AW_FULL COLLATE Latin1_General_CS_AS; ALTER DATABASE AW_FULL SET COMPATIBILITY_LEVEL = 130; ALTER DATABASE AW_FULL SET RECOVERY SIMPLE"
  if [ "$(sq -h -1 -W -d AW_FULL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables" | tr -d '[:space:]')" = 0 ]; then
    sq -b -d AW_FULL -i /aw/schema/full/01-esquema-completo.sql
  fi
  sq -d AW_FULL -Q "IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name='aw_ro') CREATE USER aw_ro FOR LOGIN aw_ro; GRANT SELECT ON SCHEMA::SYSADM TO aw_ro"
}

# WHERE de la tabla $1 (tiene columna ID: $2). Cabecera de documento = BW_<ANGEB|AUFTR|GUTSCH>_KOPF.DATUM_ERF.
donde() {
  case "$1" in
    BW_ANGEB_KOPF|BW_AUFTR_KOPF|BW_GUTSCH_KOPF) echo "WHERE DATUM_ERF >= '$DESDE'" ;;
    BW_ANGEB_*|BW_AUFTR_*|BW_GUTSCH_*) [ "$2" = 1 ] && echo "WHERE ID IN (SELECT ID FROM SYSADM.$(cut -d_ -f1,2 <<<"$1")_KOPF WHERE DATUM_ERF >= '$DESDE')" ;;
    FS_POOL) echo "WHERE ID IN (SELECT ID FROM SYSADM.FS_POOL_KOPF WHERE DATUM_ERSTELLT >= '$DESDE')" ;;
    FS_POOL_KOPF) echo "WHERE DATUM_ERSTELLT >= '$DESDE'" ;;
    BW_LOGBOOK|PD_AWBAR) echo "WHERE DATUM >= '$DESDE'" ;;
    FS_PROTOCOLL|FS_PROTOCOLL_HEAD) echo "WHERE EXECUTION >= '$DESDE'" ;;
    LG_LOGBUCH) echo "WHERE BU_DATUM >= '$DESDE'" ;;
  esac
}

tabla() {  # $1 = nombre, $2 = tiene ID (1/0)
  local t=$1 w; w=$(donde "$t" "$2" || true)
  grep -qx "$t" .cargado 2>/dev/null && return 0
  sq -b -d AW_FULL -Q "TRUNCATE TABLE SYSADM.[$t]" >/dev/null
  local out
  out=$(docker exec -e Q="SELECT * FROM SYSADM.[$t] $w" -e T="$t" -e RH -e RD -e RU -e RP -e SP $C bash -c '
    f=/tmp/bcp_$T.dat; trap "rm -f $f" EXIT
    '$BCP' "$Q" queryout $f -n -S "$RH" -d "$RD" -U "$RU" -P "$RP" -u -K ReadOnly | grep -E "rows copied|Error" &&
    '$BCP' AW_FULL.SYSADM.$T in $f -n -E -b 100000 -h TABLOCK -S localhost -U sa -P "$SP" -u | grep -E "rows copied|Error"' 2>&1) || { echo "FALLO $t: $out" >&2; return 1; }
  local n_out n_in; n_out=$(sed -n 's/^ *\([0-9]*\) rows copied.*/\1/p' <<<"$out" | sed -n 1p); n_in=$(sed -n 's/^ *\([0-9]*\) rows copied.*/\1/p' <<<"$out" | sed -n 2p)
  [ -n "$n_out" ] && [ "$n_out" = "$n_in" ] || { echo "FALLO $t: origen=${n_out:-?} destino=${n_in:-?} $out" >&2; return 1; }
  echo "$t" >> .cargado; echo "ok $t $n_in"
}

datos() {
  # tabla|tiene ID, las más grandes primero (balancea el paralelismo)
  timeout 300 "$HOME/.local/bin/aw-sql" "SET NOCOUNT ON; SELECT t.name, CASE WHEN EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=t.object_id AND c.name='ID') THEN 1 ELSE 0 END, (SELECT SUM(a.total_pages) FROM sys.partitions p JOIN sys.allocation_units a ON a.container_id=p.partition_id WHERE p.object_id=t.object_id) FROM sys.tables t WHERE t.schema_id=SCHEMA_ID('SYSADM') ORDER BY 3 DESC" |
    awk -F'|' 'NF==3 && $2 ~ /^[01]$/ { print $1, $2 }' > .tablas
  export -f tabla donde sq; export C BCP
  xargs -P "${PAR:-3}" -L1 bash -c 'tabla "$@"' _ < .tablas
}

case "${1:-todo}" in
  esquema) esquema ;;
  datos) datos ;;
  todo) esquema; datos ;;
  *) echo "uso: $0 [esquema|datos|todo]" >&2; exit 2 ;;
esac
