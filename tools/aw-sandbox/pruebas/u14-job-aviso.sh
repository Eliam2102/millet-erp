#!/bin/bash
# U1.4 · prueba del job de aviso de pedidos (docs/operacion/aw-integracion-scripts/07_job_aviso_pedidos.sql)
# contra el sandbox A+W local, con SQL Server Agent real (el job corre solo cada 15 s).
# Uso: tools/aw-sandbox/pruebas/u14-job-aviso.sh   (requiere ./aw-sandbox.sh up)
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; . ./.env; set +a
C=aw-sandbox
SCRIPTS=../../docs/operacion/aw-integracion-scripts
sq() { docker exec -i -e SQLCMDPASSWORD="$AW_SANDBOX_PASSWORD" "$C" /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa "$@"; }
cp_in() { docker cp -q "$1" "$C:/tmp/$(basename "$1")"; }

echo "== 1. Base MILLET_INTEGRACION, cola (02) y sinónimos al sandbox"
sq -d master -Q "IF DB_ID('MILLET_INTEGRACION') IS NULL CREATE DATABASE MILLET_INTEGRACION;"
cp_in "$SCRIPTS/02_create_table.sql"; sq -d master -i /tmp/02_create_table.sql
# En SER-DATA los sinónimos apuntan a MILMAIN; aquí al sandbox (07 no los toca si ya existen).
sq -d MILLET_INTEGRACION -Q "
IF OBJECT_ID('dbo.aw_src_auftr_kopf','SN') IS NOT NULL DROP SYNONYM dbo.aw_src_auftr_kopf;
IF OBJECT_ID('dbo.aw_src_auftr_hist','SN') IS NOT NULL DROP SYNONYM dbo.aw_src_auftr_hist;
CREATE SYNONYM dbo.aw_src_auftr_kopf FOR AW_SANDBOX.SYSADM.BW_AUFTR_KOPF;
CREATE SYNONYM dbo.aw_src_auftr_hist FOR AW_SANDBOX.SYSADM.BW_AUFTR_HIST;
DELETE FROM dbo.aw_solicitud_pedido WHERE numero_pedido LIKE '99000_';
IF OBJECT_ID('dbo.aw_aviso_job_pedido','U') IS NOT NULL DELETE FROM dbo.aw_aviso_job_pedido WHERE numero_pedido LIKE '99000_';"

echo "== 2. Instalar el job (07)"
cp_in "$SCRIPTS/07_job_aviso_pedidos.sql"; sq -d master -i /tmp/07_job_aviso_pedidos.sql
sq -d MILLET_INTEGRACION -Q "UPDATE dbo.aw_aviso_job_config SET desde = DATEADD(minute, -1, GETDATE()) WHERE id = 1;"

cp_in pruebas/u14-escenarios.sql
cola() { sq -d MILLET_INTEGRACION -W -s ' | ' -Q "SET NOCOUNT ON;
SELECT numero_pedido, operacion, [version], CONVERT(varchar(23), creada_at, 121) AS creada_at
  FROM dbo.aw_solicitud_pedido WHERE numero_pedido LIKE '99000_' ORDER BY numero_pedido, [version];"; }
esperar() { echo "   esperando 20 s a que el Agent corra el job..."; sleep 20; }

docker exec "$C" true
sq -d AW_SANDBOX -i /aw/schema/03-defaults-temporales.sql
trap 'sq -d AW_SANDBOX -i /aw/schema/04-quitar-defaults.sql >/dev/null' EXIT

for f in 1 2 3; do
  echo "== Fase $f"
  sq -d AW_SANDBOX -v FASE=$f -i /tmp/u14-escenarios.sql -W
  esperar
  cola
done

echo "== Fase 4: otro barrido sin cambios en A+W (no debe duplicar)"
esperar
cola

echo "== Fase 5: el botón del vendedor inserta ANTES que el job (990007: una sola fila, op 1 v1)"
sq -d AW_SANDBOX -v FASE=5 -i /tmp/u14-escenarios.sql -W
esperar
esperar
cola
n=$(sq -d MILLET_INTEGRACION -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.aw_solicitud_pedido WHERE numero_pedido = '990007';" | tr -d '[:space:]')
[ "$n" = 1 ] || { echo "FALLA: 990007 tiene $n filas, se esperaba 1 (el job duplicó el botón)" >&2; exit 1; }

echo "== Fase 6: reconfirmación posterior al botón → Modificación v2 (no se pierde)"
sq -d AW_SANDBOX -v FASE=6 -i /tmp/u14-escenarios.sql -W
esperar
cola
n=$(sq -d MILLET_INTEGRACION -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.aw_solicitud_pedido WHERE numero_pedido = '990007' AND operacion = 2 AND [version] = 2;" | tr -d '[:space:]')
[ "$n" = 1 ] || { echo "FALLA: falta la Modificación v2 de 990007" >&2; exit 1; }

echo "== Historial del job en el Agent (últimas 5 ejecuciones)"
sq -d msdb -W -s ' | ' -Q "SET NOCOUNT ON; SELECT TOP 5 h.run_date, h.run_time, h.run_status, h.run_duration
  FROM msdb.dbo.sysjobhistory h JOIN msdb.dbo.sysjobs j ON j.job_id = h.job_id
 WHERE j.name = N'Millet ERP - aviso de pedidos A+W' AND h.step_id = 0 ORDER BY h.instance_id DESC;"
