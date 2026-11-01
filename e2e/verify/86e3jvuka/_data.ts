/**
 * PT-02 test data. Orders are cloned in SQL (TEST database only) from a seeded Facturación order; the pieces are
 * A+W scans in the Demo origin tables (dbo.*), processed by the real entry worker (EscaneoProcesador) -> retention rules.
 * No row of producto_terminado is written by SQL: the retained/released state always comes from the app.
 */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { envCfg, env } from '../../_harness/session';

export const PEDIDO_CONTADO = '4800117'; // contado, 10 piezas, 8 y luego 10 escaneadas
export const PEDIDO_REPARTO = '4800118'; // REPARTO, 10 piezas, 3 y luego 10 escaneadas
export const PEDIDO_SERIE_5000 = '5000231'; // serie 5000 (siempre libera por Crédito), 10 piezas, 10 escaneadas

export function sql(statement: string): string {
  if (env !== 'test') throw new Error('[PT-02] seeding is only allowed in the test environment');
  const url = new URL(envCfg.db.url);
  if (!/_s\d+$|millet_test$/.test(url.pathname)) throw new Error(`[PT-02] refusing to seed ${url.pathname}`);
  return execFileSync('psql', [url.toString(), '-v', 'ON_ERROR_STOP=1', '-Atq', '-c', statement], {
    encoding: 'utf8',
    env: { ...process.env, PGPASSWORD: decodeURIComponent(url.password) },
  }).trim();
}

function pedidoFacturacion(numero: string, cliente: string, condicionPago: string) {
  sql(`
do $$ declare nid uuid := gen_random_uuid(); oid uuid;
begin
  select id into oid from facturacion.pedido_facturable where numero_pedido = 'DEMO-PED-NORMAL';
  if oid is null then raise exception 'seed pedido not found'; end if;
  delete from facturacion.pedido_facturable_linea where pedido_facturable_id in (select id from facturacion.pedido_facturable where numero_pedido = '${numero}');
  delete from facturacion.pedido_facturable where numero_pedido = '${numero}';
  create temp table _p on commit drop as select * from facturacion.pedido_facturable where id = oid;
  update _p set id = nid, numero_pedido = '${numero}', cliente_nombre = '${cliente}', condicion_pago_origen = '${condicionPago}';
  insert into facturacion.pedido_facturable select * from _p;
  create temp table _l on commit drop as select * from facturacion.pedido_facturable_linea where pedido_facturable_id = oid;
  update _l set id = gen_random_uuid(), pedido_facturable_id = nid, cantidad = 10;
  insert into facturacion.pedido_facturable_linea select * from _l;
end $$;`);
}

/** Demo origin tables of A+W (same DB) + the scans, one row per piece (desde..piezas). */
function escaneos(numero: string, piezas: number, desde = 1) {
  sql(`insert into dbo.pool_pos (auftnr,pos,u_pos,ae_intpos,menge) values ('${numero}',1,0,1,100) on conflict do nothing;
insert into dbo.awbar_bearbeit (etikettnr,bearb_zeit,auftnr,pos,u_pos,teile_nr,fertig,esnr_ist,bruch_flag)
select substr(encode(sha256(convert_to('${numero}-' || i, 'UTF8')), 'hex'), 1, 9),
       (now() at time zone 'America/Mexico_City') - interval '3 minutes' + (i * interval '1 second'), '${numero}', 1, 0, i::text, 1, 2821, 0
from generate_series(${desde}, ${piezas}) i;`);
}

export function prepararOrigenDemo() {
  const schema = path.resolve(process.cwd(), 'tools/aw-origen-demo/schema.sql');
  sql(fs.readFileSync(schema, 'utf8'));
  for (const t of ['awbar_bearbeit', 'pool_pos', 'pool_bearbeit', 'or_avbereich']) sql(`delete from dbo.${t} where auftnr in ('${PEDIDO_CONTADO}','${PEDIDO_REPARTO}','${PEDIDO_SERIE_5000}')`);
}

export function crearPedidos() {
  pedidoFacturacion(PEDIDO_CONTADO, 'Vidrios y Aluminios del Norte', 'CONTADO');
  pedidoFacturacion(PEDIDO_REPARTO, 'Cristales y Cancelería Monterrey', 'REPARTO');
  pedidoFacturacion(PEDIDO_SERIE_5000, 'Distribuidora de Vidrio Occidente', '30 DIAS');
}

export const escanear = escaneos;
