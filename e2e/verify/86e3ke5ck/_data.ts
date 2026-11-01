/**
 * T9.3 (86e3ke5ck) data. Pasivos arrive from CxP through an event bridge that is off in the test env, and there is no creation
 * endpoint, so suppliers, their bank accounts, the Millet bank account and the pending liabilities are seeded by SQL straight
 * into the TEST database (never dev). The corridas themselves are built / sent / signed / generated through the real UI.
 */
import { execFileSync } from 'node:child_process';
import { envCfg, env } from '../../_harness/session';

export function sql(statement: string): string {
  if (env !== 'test') throw new Error('[T9.3] seeding is only allowed in the test environment');
  const url = new URL(process.env.TEST_DATABASE_URL ?? envCfg.db.url);
  if (!/_s\d+$|millet_test$/.test(url.pathname)) throw new Error(`[T9.3] refusing to seed ${url.pathname}`);
  return execFileSync('psql', [url.toString(), '-v', 'ON_ERROR_STOP=1', '-Atq', '-c', statement], {
    encoding: 'utf8',
    env: { ...process.env, PGPASSWORD: decodeURIComponent(url.password) },
  }).trim();
}
const esc = (s: string) => s.replace(/'/g, "''");

/** CLABE with the 3-7-1 check digit (same algorithm as the backend's Clabe value object). */
export function clabe(banco: string, plaza: string, cuenta: string): string {
  const base = banco + plaza + cuenta;
  const w = [3, 7, 1];
  const s = base.split('').reduce((a, d, i) => a + ((Number(d) * w[i % 3]) % 10), 0);
  return base + String((10 - (s % 10)) % 10);
}
const roto = (c: string) => c.slice(0, 17) + String((Number(c[17]) + 3) % 10);

export type Prov = { id: string; razon: string; banco: string; clabe: string; folio: string; subtotal: number; ret: number; total: number; vence: string; grupo: 'A' | 'B' | 'C'; invalida?: boolean };

const B = { BBVA: ['012', 'BBVA México'], BANORTE: ['072', 'Banorte'], SANTANDER: ['014', 'Santander'], HSBC: ['021', 'HSBC México'], SCOTIA: ['044', 'Scotiabank'], BANAMEX: ['002', 'Citibanamex'] } as const;
type K = keyof typeof B;
// [grupo, razón social, banco, plaza, cuenta(11), folio, subtotal, retención ISR/IVA (transporte 4%)]
const DEF: Array<[Prov['grupo'], string, K, string, string, string, number, number, string]> = [
  ['A', 'Vidrio Flotado del Bajío S.A. de C.V.', 'BBVA', '180', '00412398765', 'F-48213', 86200, 0, '2026-11-04'],
  ['A', 'Perfiles de Aluminio Occidente S.A. de C.V.', 'BANORTE', '320', '00871234560', 'A-10377', 41500, 0, '2026-11-04'],
  ['A', 'Herrajes y Accesorios Industriales de Querétaro', 'BBVA', '680', '01129087654', 'HQ-2291', 18740, 0, '2026-11-05'],
  ['A', 'Selladores y Adhesivos del Norte S.A. de C.V.', 'SANTANDER', '580', '65504218873', 'SN-5508', 9860, 0, '2026-11-05'],
  ['A', 'Abrasivos y Diamantes de Monterrey S.A.', 'BBVA', '580', '00356677120', 'AM-31842', 27300, 0, '2026-11-06'],
  ['A', 'Embalajes y Tarimas de Puebla S.A. de C.V.', 'HSBC', '650', '40213398712', 'EP-7716', 12450, 0, '2026-11-06'],
  ['A', 'Gases Industriales del Centro S.A. de C.V.', 'BBVA', '180', '00654432109', 'GC-90412', 15980, 0, '2026-11-09'],
  ['A', 'Arenas Sílicas de Jalisco S.A. de C.V.', 'SCOTIA', '320', '25670011983', 'AJ-1183', 22600, 0, '2026-11-09'],
  ['A', 'Película PVB Laminados del Golfo S.A. de C.V.', 'BBVA', '300', '00778812345', 'PG-6420', 38900, 0, '2026-11-10'],
  ['A', 'Maquinados de Precisión Toluca S.A. de C.V.', 'BANAMEX', '150', '70044219876', 'MT-2057', 14320, 0, '2026-11-10'],
  ['A', 'Transportes de Carga Pesada del Pacífico S.A.', 'BBVA', '320', '01023344556', 'TC-88231', 24000, 960, '2026-11-11'],
  ['A', 'Cristales Templados de La Laguna S.A. de C.V.', 'BANORTE', '050', '11902345671', 'CL-4419', 33150, 0, '2026-11-12'],
  ['B', 'Distribuidora de Cintas y Selladores de Tijuana', 'BBVA', '020', '00231100987', 'DT-7710', 16800, 0, '2026-11-12'],
  ['B', 'Anclajes y Tornillería Industrial del Bajío', 'BBVA', '180', '00998877665', 'AT-3342', 7450, 0, '2026-11-12'],
  ['B', 'Cantera y Espejos de Guadalajara S.A. de C.V.', 'SANTANDER', '320', '65100223344', 'CE-1905', 21900, 0, '2026-11-13'],
  ['C', 'Lubricantes y Refacciones Industriales de Saltillo', 'BBVA', '050', '00445566778', 'LR-6603', 11200, 0, '2026-11-13'],
];

const uid = (n: number) => `00000009-0003-4000-8000-${String(n).padStart(12, '0')}`;
export const PROVS: Prov[] = DEF.map(([grupo, razon, bk, plaza, cta, folio, subtotal, ret, vence], i) => {
  const c = clabe(B[bk][0], plaza, cta);
  const invalida = folio === 'AT-3342';
  return { id: uid(100 + i), razon, banco: B[bk][1], clabe: invalida ? roto(c) : c, folio, subtotal, ret, total: Math.round((subtotal * 1.16 - ret) * 100) / 100, vence, grupo, invalida };
});
export const grupo = (g: Prov['grupo']) => PROVS.filter((p) => p.grupo === g);
export const sumaNeto = (ps: Prov[]) => Math.round(ps.reduce((a, p) => a + p.total, 0) * 100) / 100;
export const BANCO_MILLET = 'BBVA México';

export function seed() {
  const emp = sql('select id from compartido.empresas limit 1');
  if (!emp) throw new Error('[T9.3] no empresa in the test DB');
  sql(`delete from tesoreria.corrida_pago_archivo_linea; delete from tesoreria.corrida_pago_archivo; delete from tesoreria.corrida_pago_firma;
    delete from tesoreria.corrida_pago_linea; delete from tesoreria.corrida_pago; delete from tesoreria.pasivo_pendiente_pago; delete from tesoreria.cuenta_bancaria;`);
  sql(`insert into tesoreria.cuenta_bancaria (id, empresa_id, banco, numero_cuenta, clabe, moneda, activa, version, created_at, updated_at, titular, finalidad, sucursal)
    values ('00000009-0003-4000-8000-000000000001', '${emp}', '${BANCO_MILLET}', '0123456789', '${clabe('012', '180', '00123456789')}', 'MXN', true, 1, now(), now(),
      'Vidrios Millet S.A. de C.V.', 'Pagos a proveedores', 'MID');`);
  sql(`delete from compartido.proveedor_cuentas_bancarias where proveedor_id::text like '00000009-0003-4000-8000-%';
    delete from compartido.proveedores where id::text like '00000009-0003-4000-8000-%';`);
  PROVS.forEach((p, i) => {
    const rfc = `${p.razon.replace(/[^A-Za-z]/g, '').slice(0, 3).toUpperCase()}${String(990101 + i * 137).slice(0, 6)}${String.fromCharCode(65 + (i % 26))}${(i * 7) % 10}${String.fromCharCode(65 + ((i * 3) % 26))}`;
    sql(`insert into compartido.proveedores (id, clave, razon_social, rfc, tipo_persona, estatus, version, created_at, updated_at, condiciones_pago_dias, pais)
      values ('${p.id}', 'PRV-${String(1200 + i)}', '${esc(p.razon)}', '${rfc}', 1, 0, 1, now(), now(), 30, 'MEX');
    insert into compartido.proveedor_cuentas_bancarias (id, proveedor_id, banco, clabe, moneda, titular, predeterminada, estado, capturada_por_nombre, capturada_en, validada_por_nombre, validada_en, version, created_at, updated_at)
      values ('${uid(300 + i)}', '${p.id}', '${esc(p.banco)}', '${p.clabe}', 'MXN', '${esc(p.razon)}', true, 1, 'Compras', now(), 'Tesorería', now(), 1, now(), now());
    insert into tesoreria.pasivo_pendiente_pago (id, empresa_id, factura_proveedor_id, proveedor_id, monto_total, saldo_pendiente, moneda, fecha_vencimiento, folio_proveedor, metodo_pago,
      recibido_en, version, created_at, updated_at, beneficiario_id, origen_id, origen_tipo, tipo_beneficiario, pago_bloqueado, subtotal, impuestos_trasladados, retenciones)
      values ('${uid(500 + i)}', '${emp}', '${uid(700 + i)}', '${p.id}', ${p.total}, ${p.total}, 'MXN', '${p.vence}', '${p.folio}', 'PUE', '2026-10-28 15:00:00+00', 1, now(), now(),
      '${p.id}', '${uid(700 + i)}', 'Factura', 'Proveedor', false, ${p.subtotal}, ${Math.round(p.subtotal * 16) / 100}, ${p.ret});`);
  });
}
