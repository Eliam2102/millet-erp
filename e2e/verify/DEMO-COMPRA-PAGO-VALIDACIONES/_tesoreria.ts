/**
 * Datos de Tesorería para las validaciones: proveedores con su CLABE y su expediente, la cuenta bancaria de Millet, facturas
 * autorizadas en CxP y su pasivo pendiente de pago. El puente de eventos CxP -> Tesorería está apagado en el entorno de prueba,
 * por eso se siembra por SQL (solo BD de prueba). Las acciones bajo prueba van por la pantalla y por la API.
 */
import { sql, esc } from './_data';

export const uid = (n: number) => `00000019-0004-4000-8000-${String(n).padStart(12, '0')}`;
const TIPO_OPINION = '00000011-0001-0000-0000-000000000007';

/** CLABE con dígito verificador 3-7-1 (mismo algoritmo que el value object del backend). */
export function clabe(banco: string, plaza: string, cuenta: string): string {
  const base = banco + plaza + cuenta;
  const w = [3, 7, 1];
  const s = base.split('').reduce((a, d, i) => a + ((Number(d) * w[i % 3]) % 10), 0);
  return base + String((10 - (s % 10)) % 10);
}
const roto = (c: string) => c.slice(0, 17) + String((Number(c[17]) + 3) % 10);

export type Prov = { n: number; id: string; clave: string; razon: string; rfc: string; banco: string; clabe: string; opinion: boolean; bloqueado?: boolean; folio: string; subtotal: number; vence: string };
const DEF: Array<Omit<Prov, 'id' | 'clabe'> & { c: [string, string, string]; invalida?: boolean }> = [
  { n: 1, clave: 'PRV-3101', razon: 'Vidrio Flotado del Bajío S.A. de C.V.', rfc: 'VFB140822K59', banco: 'BBVA México', c: ['012', '180', '00412398765'], opinion: true, folio: 'F-48213', subtotal: 86200, vence: '2026-11-04' },
  { n: 2, clave: 'PRV-3102', razon: 'Selladores y Adhesivos del Norte S.A. de C.V.', rfc: 'SAN110405Q72', banco: 'Santander', c: ['014', '580', '65504218873'], opinion: false, folio: 'SN-5508', subtotal: 9860, vence: '2026-11-05' },
  { n: 3, clave: 'PRV-3103', razon: 'Perfiles de Aluminio Occidente S.A. de C.V.', rfc: 'PAO090317L38', banco: 'Banorte', c: ['072', '320', '00871234560'], opinion: true, invalida: true, folio: 'A-10377', subtotal: 41500, vence: '2026-11-04' },
  { n: 4, clave: 'PRV-3104', razon: 'Abrasivos y Diamantes de Monterrey S.A.', rfc: 'ADM120611H85', banco: 'BBVA México', c: ['012', '580', '00356677120'], opinion: true, bloqueado: true, folio: 'AM-31842', subtotal: 27300, vence: '2026-11-06' },
  { n: 5, clave: 'PRV-3105', razon: 'Gases Industriales del Centro S.A. de C.V.', rfc: 'GIC100915T26', banco: 'BBVA México', c: ['012', '180', '00654432109'], opinion: true, folio: 'GC-90412', subtotal: 15980, vence: '2026-11-09' },
  { n: 6, clave: 'PRV-3106', razon: 'Embalajes y Tarimas de Puebla S.A. de C.V.', rfc: 'ETP120611H85', banco: 'HSBC México', c: ['021', '650', '40213398712'], opinion: true, folio: 'EP-7716', subtotal: 12450, vence: '2026-11-06' },
  { n: 7, clave: 'PRV-3107', razon: 'Herrajes y Accesorios Industriales de Querétaro S.A.', rfc: 'HAI130208M77', banco: 'BBVA México', c: ['012', '680', '01129087654'], opinion: true, folio: 'HQ-2291', subtotal: 18740, vence: '2026-11-05' },
];
export const PROVS: Prov[] = DEF.map((d) => {
  const ok = clabe(...d.c);
  return { ...d, id: uid(100 + d.n), clabe: d.invalida ? roto(ok) : ok } as Prov;
});
export const prov = (n: number) => PROVS.find((p) => p.n === n)!;
export const totalDe = (p: Prov) => Math.round(p.subtotal * 1.16 * 100) / 100;
export const BANCO_MILLET = 'BBVA México';
export const CUENTA_MILLET = uid(1);

export function seed() {
  const emp = sql('select id from compartido.empresas limit 1');
  if (!emp) throw new Error('no empresa in the test DB');
  sql(`delete from tesoreria.corrida_pago_archivo_linea; delete from tesoreria.corrida_pago_archivo; delete from tesoreria.corrida_pago_firma;
    delete from tesoreria.aplicacion_pago_proveedor where corrida_id is not null; delete from tesoreria.corrida_pago_linea; delete from tesoreria.corrida_pago;
    delete from tesoreria.aplicacion_pago_proveedor where proveedor_id::text like '00000019-0004-%';
    delete from tesoreria.pasivo_pendiente_pago where proveedor_id::text like '00000019-0004-%';
    delete from cuentas_por_pagar.facturas_proveedor where proveedor_id::text like '00000019-0004-%';
    delete from compartido.adjuntos where entidad_id::text like '00000019-0004-%';
    delete from compartido.proveedor_cuentas_bancarias where proveedor_id::text like '00000019-0004-%';
    delete from compartido.proveedores where id::text like '00000019-0004-%';`);
  sql(`insert into tesoreria.cuenta_bancaria (id, empresa_id, banco, numero_cuenta, clabe, moneda, activa, version, created_at, updated_at, titular, finalidad, sucursal)
    values ('${CUENTA_MILLET}', '${emp}', '${BANCO_MILLET}', '0123456789', '${clabe('012', '180', '00123456789')}', 'MXN', true, 1, now(), now(), 'Vidrios Millet S.A. de C.V.', 'Pagos a proveedores', 'MID')
    on conflict (id) do nothing;`);
  const suc = uid(900);
  for (const p of PROVS) {
    const total = totalDe(p);
    const iva = Math.round(p.subtotal * 16) / 100;
    const uuidCfdi = uid(2000 + p.n).toUpperCase();
    sql(`insert into compartido.proveedores (id, clave, razon_social, rfc, tipo_persona, estatus, version, created_at, updated_at, condiciones_pago_dias, pais, origen)
        values ('${p.id}', '${p.clave}', '${esc(p.razon)}', '${p.rfc}', 1, 0, 1, now(), now(), 30, 'MEX', 0);
      insert into compartido.proveedor_cuentas_bancarias (id, proveedor_id, banco, clabe, moneda, titular, predeterminada, estado, capturada_por_nombre, capturada_en, validada_por_nombre, validada_en, version, created_at, updated_at)
        values ('${uid(300 + p.n)}', '${p.id}', '${esc(p.banco)}', '${p.clabe}', 'MXN', '${esc(p.razon)}', true, 1, 'Compras', now(), 'Tesorería', now(), 1, now(), now());
      insert into cuentas_por_pagar.facturas_proveedor (id, empresa_id, uuid_cfdi, proveedor_id, sucursal_id, folio_proveedor, fecha_documento, fecha_contabilizacion, fecha_vencimiento, moneda,
          subtotal, descuentos, impuestos_trasladados, retenciones, total, estado, diferencia_contra_oc, redondeo_aplicado, anticipo_aplicado_total, nc_aplicadas_total, cargos_aplicados_total, importe_pagado,
          en_revision, version, created_at, updated_at, repp_recibido, metodo_pago)
        values ('${uid(700 + p.n)}', '${emp}', '${uuidCfdi}', '${p.id}', '${suc}', '${p.folio}', now() - interval '12 days', now() - interval '12 days', '${p.vence}', 'MXN',
          ${p.subtotal}, 0, ${iva}, 0, ${total}, 3, 0, 0, 0, 0, 0, 0, false, 1, now(), now(), false, 'PUE');
      insert into tesoreria.pasivo_pendiente_pago (id, empresa_id, factura_proveedor_id, proveedor_id, monto_total, saldo_pendiente, moneda, fecha_vencimiento, uuid_cfdi, folio_proveedor, metodo_pago,
          recibido_en, version, created_at, updated_at, beneficiario_id, origen_id, origen_tipo, tipo_beneficiario, pago_bloqueado, subtotal, impuestos_trasladados, retenciones)
        values ('${uid(500 + p.n)}', '${emp}', '${uid(700 + p.n)}', '${p.id}', ${total}, ${total}, 'MXN', '${p.vence}', '${uuidCfdi}', '${p.folio}', 'PUE',
          now() - interval '10 days', 1, now(), now(), '${p.id}', '${uid(700 + p.n)}', 'Factura', 'Proveedor', false, ${p.subtotal}, ${iva}, 0);`);
    if (p.opinion)
      sql(`insert into compartido.adjuntos (id, tipo_entidad, entidad_id, tipo_documento_id, nombre_archivo, content_type, tamano_bytes, hash_sha256, blob_ref, vigente_hasta, subido_por_id, subido_en, version, created_at, updated_at)
        values ('${uid(400 + p.n)}', 'proveedor', '${p.id}', '${TIPO_OPINION}', 'opinion-32d.pdf', 'application/pdf', 20480, '${'ab'.repeat(32)}', 'opinion/${p.id}', now() + interval '20 days', '${uid(950)}', now(), 1, now(), now());`);
    if (p.bloqueado)
      sql(`update compartido.proveedores set bloqueado = true, bloqueado_en = now(), motivo_bloqueo = 'Opinión de cumplimiento negativa' where id = '${p.id}'`);
  }
}

/** Deja a un proveedor del catálogo base listo para cobrar: opinión de cumplimiento vigente (su cuenta CLABE ya viene validada del catálogo). */
export function habilitarProveedorParaPago(provId: string, n: number) {
  sql(`insert into compartido.adjuntos (id, tipo_entidad, entidad_id, tipo_documento_id, nombre_archivo, content_type, tamano_bytes, hash_sha256, blob_ref, vigente_hasta, subido_por_id, subido_en, version, created_at, updated_at)
      values ('${uid(450 + n)}', 'proveedor', '${provId}', '${TIPO_OPINION}', 'opinion-32d.pdf', 'application/pdf', 20480, '${'ab'.repeat(32)}', 'opinion/${provId}', now() + interval '20 days', '${uid(950)}', now(), 1, now(), now()) on conflict (id) do nothing;`);
}

/** Cuenta bancaria de Millet para pagos (idempotente). */
export function sembrarCuentaMillet() {
  const emp = sql('select id from compartido.empresas limit 1');
  sql(`insert into tesoreria.cuenta_bancaria (id, empresa_id, banco, numero_cuenta, clabe, moneda, activa, version, created_at, updated_at, titular, finalidad, sucursal)
    values ('${CUENTA_MILLET}', '${emp}', '${BANCO_MILLET}', '0123456789', '${clabe('012', '180', '00123456789')}', 'MXN', true, 1, now(), now(), 'Vidrios Millet S.A. de C.V.', 'Pagos a proveedores', 'MID')
    on conflict (id) do nothing;`);
}

/** Pasivo pendiente de pago de una factura de CxP ya existente (el puente de eventos está apagado en la BD de prueba). */
export function sembrarPasivo(o: { facturaId: string; proveedorId: string; total: number; subtotal: number; folio: string; uuidCfdi: string; n: number }) {
  const emp = sql('select id from compartido.empresas limit 1');
  const iva = Math.round((o.total - o.subtotal) * 100) / 100;
  sql(`insert into tesoreria.pasivo_pendiente_pago (id, empresa_id, factura_proveedor_id, proveedor_id, monto_total, saldo_pendiente, moneda, fecha_vencimiento, uuid_cfdi, folio_proveedor, metodo_pago,
      recibido_en, version, created_at, updated_at, beneficiario_id, origen_id, origen_tipo, tipo_beneficiario, pago_bloqueado, subtotal, impuestos_trasladados, retenciones)
    values ('${uid(550 + o.n)}', '${emp}', '${o.facturaId}', '${o.proveedorId}', ${o.total}, ${o.total}, 'MXN', now() + interval '20 days', '${o.uuidCfdi}', '${o.folio}', 'PUE',
      now(), 1, now(), now(), '${o.proveedorId}', '${o.facturaId}', 'Factura', 'Proveedor', false, ${o.subtotal}, ${iva}, 0)
    on conflict do nothing;`); // with the Outbox on, CxP already sent the liability to Tesorería
}
