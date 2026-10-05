#!/bin/bash
# Emite a stdout los índices secundarios (no PK) de TODAS las tablas SYSADM de la A+W real. Solo SELECT sobre sys.* vía aw-sql. Sin datos.
# Uso: tools/aw-sandbox/extraer-indices.sh > tools/aw-sandbox/schema/full/02-indices.sql
set -euo pipefail
SQL=${AW_SQL:-$HOME/.local/bin/aw-sql}
echo "-- Generado por tools/aw-sandbox/extraer-indices.sh (solo estructura). Índices secundarios de SYSADM."
timeout 300 "$SQL" "SET NOCOUNT ON; SELECT 'I', t.name, i.name, CASE WHEN i.is_unique=1 THEN 'UNIQUE ' ELSE '' END + i.type_desc, CASE WHEN ic.is_included_column=1 THEN 'INC' ELSE 'KEY' END,
  '[' + c.name + ']' + CASE WHEN ic.is_descending_key=1 AND ic.is_included_column=0 THEN ' DESC' ELSE '' END
  FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=0 AND i.type>0 AND i.is_hypothetical=0 AND i.has_filter=0
  JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
  WHERE t.schema_id=SCHEMA_ID('SYSADM') ORDER BY t.name, i.name, ic.is_included_column, ic.key_ordinal, ic.index_column_id" |
awk -F'|' '$1=="I" && NF>=6' |
awk -F'|' '
  function flush() { if (cur!="") { printf "CREATE %s INDEX [%s] ON SYSADM.[%s] (%s)%s;\n", tipo, nombre, tabla, k, (inc!="" ? " INCLUDE (" inc ")" : "") } }
  { id=$2 "." $3; if (id!=cur) { flush(); cur=id; tabla=$2; nombre=$3; tipo=$4; sub(/ *CLUSTERED/, " CLUSTERED", tipo); k=""; inc="" }
    if ($5=="KEY") k = (k=="" ? $6 : k ", " $6); else inc = (inc=="" ? $6 : inc ", " $6) }
  END { flush() }' | sed 's/NON CLUSTERED/NONCLUSTERED/'
echo "GO"
