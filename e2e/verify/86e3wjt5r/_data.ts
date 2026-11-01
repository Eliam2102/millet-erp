/**
 * Datos de 86e3wjt5r (NC de clientes desde A+W). La ingesta es la real: el worker AwSolicitudesWorker (encendido en
 * verify.config.json local) lee la tabla puente demo `dbo.aw_solicitud_nota_credito` (BD millet_aw_origen_nc, mismo Postgres)
 * y manda cada fila a ProcesarNotaCreditoAwHandler. Lo único sembrado por SQL en la BD de pruebas del ERP son los
 * antecedentes que en producción vienen de A+W/PAC: cliente, pedido y facturas ya timbradas (no hay PAC en este ambiente).
 */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import { env, envCfg } from '../../_harness/session';

const url = () => new URL(envCfg.db.url);
function psql(db: string, statement: string, file = false): string {
  if (env !== 'test') throw new Error('[86e3wjt5r] solo se siembra en el ambiente test');
  const u = url();
  if (!/^(millet_test|millet_aw_origen_nc)$/.test(db)) throw new Error(`[86e3wjt5r] BD no permitida: ${db}`);
  u.pathname = `/${db}`;
  return execFileSync('psql', [u.toString(), '-v', 'ON_ERROR_STOP=1', '-Atq', file ? '-f' : '-c', statement], {
    encoding: 'utf8', env: { ...process.env, PGPASSWORD: decodeURIComponent(u.password) },
  }).trim();
}
export const sql = (s: string) => psql('millet_test', s);
export const sqlAw = (s: string) => psql('millet_aw_origen_nc', s);
const q = (s: string) => `'${s.replace(/'/g, "''")}'`;

/** INSERT con las columnas NOT NULL sin default rellenadas por tipo; `v` pisa lo que se indique (valores SQL crudos). */
function insertar(tabla: string, v: Record<string, string>) {
  const [schema, t] = tabla.split('.');
  const cols = sql(`select column_name||'|'||data_type from information_schema.columns where table_schema='${schema}' and table_name='${t}'
    and ((is_nullable='NO' and column_default is null) or false) order by ordinal_position`).split('\n').filter(Boolean);
  const vals: Record<string, string> = {};
  for (const c of cols) {
    const [n, ty] = c.split('|');
    vals[n] = /uuid/.test(ty) ? 'gen_random_uuid()' : /timestamp/.test(ty) ? 'now()' : /boolean/.test(ty) ? 'false'
      : /(int|numeric)/.test(ty) ? '0' : /json/.test(ty) ? "'{}'" : "''";
  }
  Object.assign(vals, v);
  sql(`insert into ${tabla} (${Object.keys(vals).join(',')}) values (${Object.values(vals).join(',')})`);
}

export const CLIENTE_REF = 'CLI-AW-48210';
export const CLIENTE = 'Vidrios y Cristales del Bajío, S.A. de C.V.';
export const FACTURA_A = 'A-1042'; // saldo 5,000: recibe la NC de 1,500
export const FACTURA_B = 'A-1043'; // saldo 5,000: la NC de 9,000 la excede
export const NC_OK = 'NC-AW-20417';
export const NC_SIN_FACTURA = 'NC-AW-20418';
export const NC_EXCEDE = 'NC-AW-20419';

export function seed() {
  const emp = sql('select id from compartido.empresas limit 1');
  const suc = sql('select id from compartido.sucursales order by clave limit 1');
  if (!emp || !suc) throw new Error('[86e3wjt5r] sin empresa/sucursal en la BD de pruebas');
  sql(`update compartido.parametros_globales set valor='Demo' where clave='integraciones.aw.origen-activo'`);
  // BD puente demo desde cero
  if (!execFileSync('psql', [url().toString().replace(/\/[^/]*$/, '/postgres'), '-Atq', '-c', "select 1 from pg_database where datname='millet_aw_origen_nc'"],
    { encoding: 'utf8', env: { ...process.env, PGPASSWORD: decodeURIComponent(url().password) } }).trim()) throw new Error('[86e3wjt5r] falta la BD millet_aw_origen_nc');
  sqlAw('delete from dbo.aw_solicitud_nota_credito');

  // limpia corridas previas (la BD de pruebas se resetea por corrida; esto cubre re-ejecuciones con SKIP_ENV_UP)
  sql(`delete from facturacion.bandeja_excepcion_importacion where pedido_ref like 'NC %';
       delete from facturacion.nota_credito; delete from facturacion.comprobante where tipo=2; delete from facturacion.factura_venta where id in (select id from facturacion.comprobante where folio in ('${FACTURA_A}','${FACTURA_B}'));
       delete from facturacion.comprobante where folio in ('${FACTURA_A}','${FACTURA_B}');
       delete from facturacion.pedido_facturable where numero_pedido in ('4500871','4500872');
       delete from compartido.clientes where referencia_externa='${CLIENTE_REF}';`);

  const cli = '0a1b2c3d-0000-4000-8000-000000004821';
  insertar('compartido.clientes', { id: q(cli), clave: "'C-48210'", referencia_externa: q(CLIENTE_REF), razon_social: q(CLIENTE), rfc: "'VCB100315AB4'",
    regimen_fiscal: "'601'", codigo_postal_fiscal: "'37530'", uso_cfdi_default: "'G03'", forma_pago_default: "'99'", metodo_pago_default: "'PPD'",
    moneda_default: "'MXN'", es_generico: 'false', origen: '1', estatus: '1', version: '1' });
  for (const [i, [folio, ped]] of [[FACTURA_A, '4500871'], [FACTURA_B, '4500872']].entries()) {
    const idPed = `0a1b2c3d-0000-4000-8000-0000000048${30 + i}`, idFac = `0a1b2c3d-0000-4000-8000-0000000048${40 + i}`;
    insertar('facturacion.pedido_facturable', { id: q(idPed), empresa_id: q(emp), origen: '1', numero_pedido: q(ped), sucursal_id: q(suc), cliente_id: q(cli),
      cliente_nombre: q(CLIENTE), canal_venta: '1', comportamiento_fiscal: '1', moneda: "'MXN'", total: '5000', estado: '3', version: '1' });
    insertar('facturacion.comprobante', { id: q(idFac), empresa_id: q(emp), tipo: '1', folio: q(folio), folio_numero: String(1042 + i), sucursal_id: q(suc),
      receptor_rfc: "'VCB100315AB4'", receptor_nombre: q(CLIENTE), receptor_regimen_fiscal: "'601'", receptor_codigo_postal: "'37530'", receptor_uso_cfdi: "'G03'",
      receptor_pais: "'MEX'", rfc_emisor: "'VMI080915XY2'", regimen_fiscal_emisor: "'601'", nombre_emisor: "'Vidrios Millet'", lugar_expedicion: "'64000'",
      metodo_pago: "'PPD'", forma_pago: "'99'", moneda: "'MXN'", subtotal: '4310.34', descuento: '0', impuestos_trasladados: '689.66', retenciones: '0', total: '5000',
      estado: '4', periodo_anio: '2026', periodo_mes: '10', uuid: q(`6f3a1c52-8d1e-4b7a-9c20-5e1d0a7b${4210 + i}`), fecha_timbrado: "'2026-10-20 15:30:00+00'", version: '1' });
    insertar('facturacion.factura_venta', { id: q(idFac), comportamiento_fiscal: '1', pedido_facturable_id: q(idPed), factura_agrupada: 'false' });
  }

}

export function ncAw(numero: string, factura: string, pedido: string, importe: number, descripcion: string, tipo = 'Bonificacion', version = 1) {
  sqlAw(`insert into dbo.aw_solicitud_nota_credito (numero_nota, version, numero_pedido, factura_folio, cliente_ref, tipo_nota, importe, descripcion)
    values (${q(numero)}, ${version}, ${q(pedido)}, ${q(factura)}, ${q(CLIENTE_REF)}, '${tipo}', ${importe}, ${q(descripcion)})`);
}
/** Espera a que el worker procese (acuse en la tabla puente). */
export async function acuse(numero: string, version = 1, timeoutMs = 60_000) {
  const fin = Date.now() + timeoutMs;
  for (;;) {
    const r = sqlAw(`select coalesce(resultado::text,'')||'|'||coalesce(motivo,'') from dbo.aw_solicitud_nota_credito where numero_nota=${q(numero)} and version=${version}`);
    if (r && !r.startsWith('|')) return { resultado: Number(r.split('|')[0]), motivo: r.slice(r.indexOf('|') + 1) };
    if (Date.now() > fin) throw new Error(`[86e3wjt5r] el worker no procesó ${numero} en ${timeoutMs} ms`);
    await new Promise((r) => setTimeout(r, 1000));
  }
}
export const ncEnErp = (numero: string) => Number(sql(`select count(*) from facturacion.nota_credito where aw_numero_nota=${q(numero)}`));

/** Sucursal de las facturas sembradas (la de la NC). */
export const sucursalId = () => sql('select id from compartido.sucursales order by clave limit 1');
