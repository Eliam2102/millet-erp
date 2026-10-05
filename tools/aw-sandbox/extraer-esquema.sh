#!/bin/bash
# Emite a stdout el DDL T-SQL (SYSADM) de las tablas/columnas que usan los lectores AwClientesSqlOrigen y
# AwProductosSqlOrigen. Solo SELECT sobre INFORMATION_SCHEMA/sys vía aw-sql. Sin datos.
# Uso: tools/aw-sandbox/extraer-esquema.sh > tools/aw-sandbox/schema/01-esquema.sql
set -euo pipefail
SQL=${AW_SQL:-$HOME/.local/bin/aw-sql}

# tabla:col,col,...  (mantener alineado con los lectores)
TABLAS=(
 "KU_KUNDEN:ID,MANDANT,NAME1,NAME2,NAME3,STRASSE,ORT,PLZ,PROVINZ,LAND,UST_ID,STEUERNUMMER,TLF1,TLF2,MAIL,ZAHLBED,WAEHRUNG,KREDIT_LIMIT,KREDIT_LIMIT1,KREDIT_LIMIT_NET,KZ_STATUS,KZ_GESPERRT,DATUM,TRANSACTION_TIME"
 "KA_ZAHLBED:BEZ,NUMMER,BRUTTOTAGE"
 "BA_PRODUKTE:BA_PRODUKT,BA_MCODE,KZ_GESPERRT,BA_MASS_DICKE,BA_STD_HOEHE,BA_STD_BREITE,TRANSACTION_TIME,BA_PRODUKTART"
 "BA_PRODUKTE_BEZ:BA_PRODUKT,SPRACH_ID,BA_BEZ1,BA_BEZ2,BA_BEZ3,BA_MENGENEINH"
 "BA_STUKL:PRODUKT,BOM_POS,BOM_PRODUKT,BOM_LEVEL"
)

q() { timeout 120 "$SQL" "$1" | awk -F'|' 'NF>=3 && $1!="" && $1 !~ /^-+$/' ; }

echo "-- Generado por tools/aw-sandbox/extraer-esquema.sh (solo estructura, sin datos)."
echo "-- Columnas limitadas a las que usan los lectores AwClientesSqlOrigen / AwProductosSqlOrigen."
echo "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'SYSADM') EXEC('CREATE SCHEMA SYSADM');"
echo "GO"

for t in "${TABLAS[@]}"; do
  tabla=${t%%:*}; cols=${t#*:}
  lista="'${cols//,/\',\'}'"
  echo
  echo "CREATE TABLE SYSADM.$tabla ("
  # T|col|tipo|len|prec|escala|dtprec|nullable|collation
  q "SELECT 'C', COLUMN_NAME, DATA_TYPE, ISNULL(CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)),'-'), ISNULL(CAST(NUMERIC_PRECISION AS varchar(10)),'-'), ISNULL(CAST(NUMERIC_SCALE AS varchar(10)),'-'), ISNULL(CAST(DATETIME_PRECISION AS varchar(10)),'-'), IS_NULLABLE, ISNULL(COLLATION_NAME,'-') FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='SYSADM' AND TABLE_NAME='$tabla' AND COLUMN_NAME IN ($lista) ORDER BY ORDINAL_POSITION" |
  awk -F'|' -v n="$(echo "$cols" | tr ',' '\n' | wc -l)" '
    $1=="C" { c++; tipo=$3
      if (tipo ~ /char|binary/) { tipo = tipo "(" ($4==-1 ? "max" : $4) ")" }
      else if (tipo ~ /^(decimal|numeric)$/) { tipo = tipo "(" $5 "," $6 ")" }
      else if (tipo ~ /^(datetime2|datetimeoffset|time)$/) { tipo = tipo "(" $7 ")" }
      col = "    [" $2 "] " tipo
      if ($9 != "-") col = col " COLLATE " $9
      col = col ($8=="YES" ? " NULL" : " NOT NULL")
      lines[c]=col }
    END { if (c!=n) { print "!! columnas encontradas " c " de " n > "/dev/stderr"; exit 1 }
      for (i=1;i<=c;i++) print lines[i] (i<c ? "," : "") }'
  echo ");"
  # Índices: I|nombre|pk|unique|clustered|ord|col|desc|incluida   (solo se emiten si TODAS sus columnas están en el conjunto)
  q "SELECT 'I', i.name, CAST(i.is_primary_key AS varchar(1)), CAST(i.is_unique AS varchar(1)), i.type_desc, CAST(ic.key_ordinal AS varchar(3)), c.name, CAST(ic.is_descending_key AS varchar(1)), CAST(ic.is_included_column AS varchar(1)) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('SYSADM.$tabla') AND i.type>0 AND i.is_hypothetical=0 ORDER BY i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id" |
  awk -F'|' -v cols=",$cols," -v tabla="$tabla" '
    function flush(   k,s,inc,ok,i) {
      if (nom=="") return
      ok = 1; s=""; inc=""
      for (i=1;i<=nk;i++) { if (index(cols, "," kc[i] ",")==0) ok=0; s = s (i>1?", ":"") "[" kc[i] "]" kd[i] }
      for (i=1;i<=ni;i++) { if (index(cols, "," ic_[i] ",")==0) ok=0; inc = inc (i>1?", ":"") "[" ic_[i] "]" }
      if (!ok) { print "-- omitido (usa columnas fuera del conjunto): " nom " (" (pk?"PK":"IX") ")"; return }
      if (pk) print "ALTER TABLE SYSADM." tabla " ADD CONSTRAINT [" nom "] PRIMARY KEY " (cl=="CLUSTERED"?"CLUSTERED":"NONCLUSTERED") " (" s ");"
      else print "CREATE " (un?"UNIQUE ":"") (cl=="CLUSTERED"?"CLUSTERED":"NONCLUSTERED") " INDEX [" nom "] ON SYSADM." tabla " (" s ")" (inc!=""?" INCLUDE (" inc ")":"") ";"
    }
    $1=="I" { if ($2!=nom) { flush(); nom=$2; pk=$3; un=$4; cl=$5; nk=0; ni=0 }
      if ($9==1) ic_[++ni]=$7; else { kc[++nk]=$7; kd[nk]=($8==1?" DESC":"") } }
    END { flush() }'
  echo "GO"
done
