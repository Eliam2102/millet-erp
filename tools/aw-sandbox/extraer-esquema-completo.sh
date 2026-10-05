#!/bin/bash
# Emite a stdout el DDL T-SQL de TODAS las tablas SYSADM de la A+W real (columnas, IDENTITY, PK). Solo SELECT sobre sys.* vía aw-sql. Sin datos.
# Sin índices secundarios, defaults ni checks (ponytail: el sandbox es de lectura; agregarlos si alguna consulta lo pide).
# Uso: tools/aw-sandbox/extraer-esquema-completo.sh > tools/aw-sandbox/schema/full/01-esquema-completo.sql
set -euo pipefail
SQL=${AW_SQL:-$HOME/.local/bin/aw-sql}
q() { timeout 300 "$SQL" "$1" | awk -F'|' '($1=="C"||$1=="P") && NF>=3'; }  # ponytail: sqlcmd corta cada fila a 256 car.
FROM="FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id"

echo "-- Generado por tools/aw-sandbox/extraer-esquema-completo.sh (solo estructura, sin datos). BD AW_FULL (CS_AS por defecto)."
echo "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'SYSADM') EXEC('CREATE SCHEMA SYSADM');"
echo "GO"

# C|tabla|definición de columna
q "SET NOCOUNT ON; SELECT 'C', t.name, '[' + c.name + '] ' + ty.name
  + CASE WHEN ty.name IN ('char','varchar','nchar','nvarchar','binary','varbinary') THEN '(' + CASE WHEN c.max_length=-1 THEN 'max' WHEN ty.name IN ('nchar','nvarchar') THEN CAST(c.max_length/2 AS varchar(6)) ELSE CAST(c.max_length AS varchar(6)) END + ')'
         WHEN ty.name IN ('decimal','numeric') THEN '(' + CAST(c.precision AS varchar(3)) + ',' + CAST(c.scale AS varchar(3)) + ')'
         WHEN ty.name IN ('datetime2','time','datetimeoffset') THEN '(' + CAST(c.scale AS varchar(3)) + ')' ELSE '' END
  + ISNULL(' IDENTITY(' + CAST(ic.seed_value AS varchar(20)) + ',' + CAST(ic.increment_value AS varchar(20)) + ')', '')
  + CASE WHEN c.is_nullable=1 THEN ' NULL' ELSE ' NOT NULL' END
  $FROM LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
  WHERE t.schema_id=SCHEMA_ID('SYSADM') ORDER BY t.name, c.column_id" |
awk -F'|' '
  $2!=cur { if (cur!="") print "\n);"; cur=$2; printf "\nCREATE TABLE SYSADM.[%s] (\n", cur; n=0 }
  { printf "%s    %s", (n++ ? ",\n" : ""), $3 }
  END { if (cur!="") print "\n);" }'
echo "GO"

# P|tabla|nombre PK|tipo|columna (una fila por columna: sqlcmd trunca líneas largas) → ALTER tras los CREATE (tablas vacías, instantáneo)
q "SET NOCOUNT ON; SELECT 'P', t.name, i.name, i.type_desc, '[' + c.name + ']' + CASE WHEN ic.is_descending_key=1 THEN ' DESC' ELSE '' END
  FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=1 JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=0
  JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
  WHERE t.schema_id=SCHEMA_ID('SYSADM') ORDER BY t.name, ic.key_ordinal" |
awk -F'|' '$1=="P" && NF>=5 {
  if ($2!=cur) { if (cur!="") print ");"; cur=$2; printf "ALTER TABLE SYSADM.[%s] ADD CONSTRAINT [%s] PRIMARY KEY %s (%s", $2, $3, $4, $5 }
  else printf ", %s", $5 }
  END { if (cur!="") print ");" }'
echo "GO"
