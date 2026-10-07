#!/bin/bash
# Perfil SIN PII de las tablas A+W usadas por los lectores: conteos, distribuciones de catálogo, nulos/ceros/vacíos,
# longitudes máximas. Misma salida (metrica|valor, ordenada binaria) para real y sandbox => diff directo.
# Uso: perfilar.sh <real|sandbox>
#  real    -> ~/.local/bin/aw-sql (solo SELECT)
#  sandbox -> sqlcmd en contenedor efímero (docker run --network host) contra 127.0.0.1,${AW_SANDBOX_PORT:-14330}
#             env: AW_SANDBOX_USER (def. sa), AW_SANDBOX_PASSWORD (obligatoria), AW_SANDBOX_DB (def. AW_SANDBOX),
#                  AW_SANDBOX_PORT, AW_SANDBOX_IMAGE
set -euo pipefail

case "${1:-}" in
  real) run() { timeout 180 "$HOME/.local/bin/aw-sql" "$1"; } ;;
  sandbox)
    : "${AW_SANDBOX_PASSWORD:?define AW_SANDBOX_PASSWORD}"
    run() { timeout 180 docker run --rm --network host -e SQLCMDPASSWORD="$AW_SANDBOX_PASSWORD" \
      "${AW_SANDBOX_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}" /opt/mssql-tools18/bin/sqlcmd \
      -S "127.0.0.1,${AW_SANDBOX_PORT:-14330}" -d "${AW_SANDBOX_DB:-AW_SANDBOX}" -U "${AW_SANDBOX_USER:-sa}" \
      -C -W -s"|" -Q "$1"; } ;;
  *) echo "uso: $0 <real|sandbox>" >&2; exit 2 ;;
esac

# tabla  n=numéricas  s=texto  d=fechas
declare -A N S D
N[KU_KUNDEN]="ID MANDANT KZ_STATUS KZ_GESPERRT KREDIT_LIMIT KREDIT_LIMIT1 KREDIT_LIMIT_NET"
S[KU_KUNDEN]="NAME1 NAME2 NAME3 STRASSE ORT PLZ PROVINZ LAND UST_ID STEUERNUMMER TLF1 TLF2 MAIL ZAHLBED WAEHRUNG"
D[KU_KUNDEN]="DATUM TRANSACTION_TIME"
N[KA_ZAHLBED]="NUMMER BRUTTOTAGE"; S[KA_ZAHLBED]="BEZ"; D[KA_ZAHLBED]=""
N[BA_PRODUKTE]="BA_PRODUKT KZ_GESPERRT BA_MASS_DICKE BA_STD_HOEHE BA_STD_BREITE"
S[BA_PRODUKTE]="BA_MCODE BA_PRODUKTART"; D[BA_PRODUKTE]="TRANSACTION_TIME"
N[BA_PRODUKTE_BEZ]="BA_PRODUKT SPRACH_ID"; S[BA_PRODUKTE_BEZ]="BA_BEZ1 BA_BEZ2 BA_BEZ3 BA_MENGENEINH"; D[BA_PRODUKTE_BEZ]=""
N[BA_STUKL]="PRODUKT BOM_POS BOM_PRODUKT BOM_LEVEL"; S[BA_STUKL]=""; D[BA_STUKL]=""

parts=()
add() { parts+=("$1"); }

# Conteos (sys.partitions)
add "SELECT 'filas.' + t.name AS m, CAST(SUM(p.rows) AS bigint) AS v FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id<=1 WHERE SCHEMA_NAME(t.schema_id)='SYSADM' AND t.name IN ('KU_KUNDEN','KA_ZAHLBED','BA_PRODUKTE','BA_PRODUKTE_BEZ','BA_STUKL') GROUP BY t.name"

# Nulos/ceros/vacíos/longitud máx. por columna: un barrido por tabla
for t in KU_KUNDEN KA_ZAHLBED BA_PRODUKTE BA_PRODUKTE_BEZ BA_STUKL; do
  aggs=(); vals=(); i=0
  for c in ${N[$t]}; do
    aggs+=("SUM(CASE WHEN [$c] IS NULL THEN 1 ELSE 0 END) a$i" "SUM(CASE WHEN [$c] = 0 THEN 1 ELSE 0 END) a$((i+1))")
    vals+=("('$t.$c.nulos',a$i)" "('$t.$c.ceros',a$((i+1)))"); i=$((i+2))
  done
  for c in ${S[$t]}; do
    aggs+=("SUM(CASE WHEN [$c] IS NULL THEN 1 ELSE 0 END) a$i" "SUM(CASE WHEN LTRIM(RTRIM([$c])) = N'' THEN 1 ELSE 0 END) a$((i+1))" "MAX(LEN([$c])) a$((i+2))")
    vals+=("('$t.$c.nulos',a$i)" "('$t.$c.vacios',a$((i+1)))" "('$t.$c.longmax',a$((i+2)))"); i=$((i+3))
  done
  for c in ${D[$t]}; do
    aggs+=("SUM(CASE WHEN [$c] IS NULL THEN 1 ELSE 0 END) a$i"); vals+=("('$t.$c.nulos',a$i)"); i=$((i+1))
  done
  add "SELECT v.m, CAST(ISNULL(v.v,0) AS bigint) FROM (SELECT $(IFS=,; echo "${aggs[*]}") FROM SYSADM.$t WITH (NOLOCK)) x CROSS APPLY (VALUES $(IFS=,; echo "${vals[*]}")) v(m,v)"
done

# Distribuciones de catálogo (valor|frecuencia). NULL se muestra como <null>.
dist() { # tabla columna [where]
  add "SELECT 'dist.$1.$2[' + ISNULL(CAST([$2] AS nvarchar(100)),N'<null>') + ']', CAST(COUNT(*) AS bigint) FROM SYSADM.$1 WITH (NOLOCK) ${3:+WHERE $3} GROUP BY [$2]"
}
dist KU_KUNDEN WAEHRUNG; dist KU_KUNDEN ZAHLBED; dist KU_KUNDEN KZ_STATUS; dist KU_KUNDEN KZ_GESPERRT
dist BA_PRODUKTE KZ_GESPERRT; dist BA_PRODUKTE BA_PRODUKTART
dist BA_PRODUKTE_BEZ BA_MENGENEINH "SPRACH_ID = 0"; dist BA_PRODUKTE_BEZ SPRACH_ID
dist BA_STUKL BOM_LEVEL

# Reglas/rarezas (solo conteos)
add "SELECT m, CAST(v AS bigint) FROM (SELECT
  (SELECT COUNT(*) FROM (SELECT BEZ FROM SYSADM.KA_ZAHLBED GROUP BY BEZ HAVING COUNT(*)>1) d) AS [reg.KA_ZAHLBED.BEZ_duplicados_exactos],
  (SELECT COUNT(*) FROM (SELECT BEZ COLLATE Latin1_General_CI_AS b FROM SYSADM.KA_ZAHLBED GROUP BY BEZ COLLATE Latin1_General_CI_AS HAVING COUNT(*)>1) d) AS [reg.KA_ZAHLBED.BEZ_duplicados_ci],
  (SELECT COUNT(*) FROM SYSADM.KU_KUNDEN k WITH (NOLOCK) WHERE NOT EXISTS (SELECT 1 FROM SYSADM.KA_ZAHLBED z WHERE z.BEZ=k.ZAHLBED)) AS [reg.KU_KUNDEN.ZAHLBED_sin_catalogo],
  (SELECT COUNT(*) FROM SYSADM.KU_KUNDEN k WITH (NOLOCK) WHERE (SELECT COUNT(*) FROM SYSADM.KA_ZAHLBED z WHERE z.BEZ=k.ZAHLBED)>1) AS [reg.KU_KUNDEN.ZAHLBED_multiples_en_catalogo],
  (SELECT COUNT(*) FROM (SELECT UST_ID FROM SYSADM.KU_KUNDEN WITH (NOLOCK) WHERE UST_ID IS NOT NULL AND LTRIM(RTRIM(UST_ID))<>N'' GROUP BY UST_ID HAVING COUNT(*)>1) d) AS [reg.KU_KUNDEN.UST_ID_grupos_repetidos],
  (SELECT ISNULL(SUM(c),0) FROM (SELECT COUNT(*) c FROM SYSADM.KU_KUNDEN WITH (NOLOCK) WHERE UST_ID IS NOT NULL AND LTRIM(RTRIM(UST_ID))<>N'' GROUP BY UST_ID HAVING COUNT(*)>1) d) AS [reg.KU_KUNDEN.UST_ID_filas_en_grupos_repetidos],
  (SELECT COUNT(*) FROM SYSADM.BA_PRODUKTE WITH (NOLOCK) WHERE BA_PRODUKT = 0) AS [reg.BA_PRODUKTE.BA_PRODUKT_cero],
  (SELECT COUNT(*) FROM (SELECT BA_MCODE FROM SYSADM.BA_PRODUKTE WITH (NOLOCK) WHERE BA_MCODE IS NOT NULL GROUP BY BA_MCODE HAVING COUNT(*)>1) d) AS [reg.BA_PRODUKTE.BA_MCODE_grupos_repetidos],
  (SELECT COUNT(*) FROM SYSADM.BA_PRODUKTE p WITH (NOLOCK) WHERE NOT EXISTS (SELECT 1 FROM SYSADM.BA_PRODUKTE_BEZ b WHERE b.BA_PRODUKT=p.BA_PRODUKT AND b.SPRACH_ID=0)) AS [reg.BA_PRODUKTE.sin_BEZ_sprach0],
  (SELECT COUNT(*) FROM SYSADM.BA_STUKL s WITH (NOLOCK) WHERE s.BOM_LEVEL=1) AS [reg.BA_STUKL.nivel1_filas],
  (SELECT COUNT(DISTINCT s.PRODUKT) FROM SYSADM.BA_STUKL s WITH (NOLOCK) WHERE s.BOM_LEVEL=1) AS [reg.BA_STUKL.nivel1_productos],
  (SELECT COUNT(*) FROM SYSADM.BA_STUKL s WITH (NOLOCK) WHERE s.BOM_LEVEL=1 AND NOT EXISTS (SELECT 1 FROM SYSADM.BA_PRODUKTE c WHERE c.BA_PRODUKT=s.BOM_PRODUKT)) AS [reg.BA_STUKL.nivel1_componente_huerfano]
  ) x UNPIVOT (v FOR m IN ([reg.KA_ZAHLBED.BEZ_duplicados_exactos],[reg.KA_ZAHLBED.BEZ_duplicados_ci],[reg.KU_KUNDEN.ZAHLBED_sin_catalogo],[reg.KU_KUNDEN.ZAHLBED_multiples_en_catalogo],[reg.KU_KUNDEN.UST_ID_grupos_repetidos],[reg.KU_KUNDEN.UST_ID_filas_en_grupos_repetidos],[reg.BA_PRODUKTE.BA_PRODUKT_cero],[reg.BA_PRODUKTE.BA_MCODE_grupos_repetidos],[reg.BA_PRODUKTE.sin_BEZ_sprach0],[reg.BA_STUKL.nivel1_filas],[reg.BA_STUKL.nivel1_productos],[reg.BA_STUKL.nivel1_componente_huerfano])) u"

sql="SET NOCOUNT ON; SELECT m, v FROM ("
for i in "${!parts[@]}"; do [ "$i" -gt 0 ] && sql+=" UNION ALL "; sql+="SELECT CAST(m AS nvarchar(300)) m, CAST(v AS bigint) v FROM (${parts[$i]}) q$i(m,v)"; done
sql+=") r ORDER BY m COLLATE Latin1_General_BIN2"

echo "# perfil $1 (sin PII; metrica|valor)"
out=$(run "$sql")
datos=$(printf '%s\n' "$out" | awk -F'|' 'NF==2 && $1!="m" && $1 !~ /^-+$/')
[ -n "$datos" ] || { echo "perfilar: sin datos; salida cruda:" >&2; printf '%s\n' "$out" >&2; exit 1; }
printf '%s\n' "$datos"
