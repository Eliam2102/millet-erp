/**
 * 86e3qddce (F1-FAC-06) test data. The cross-module events (Tesorería -> Facturación pending REP, Facturación -> CxC
 * projection) travel over the Outbox / Service Bus, which are OFF in the test env, so the rows those listeners would
 * write are seeded with SQL straight into the test DB. The screens and the emission itself are real.
 */
import { execFileSync } from 'node:child_process';
import { envCfg, env, slot } from '../../_harness/session';

export const CLIENTE = '0c5d0e10-86e3-4d00-8000-000000000001';
export const FACTURA = '0c5d0e10-86e3-4d00-8000-000000000002';
export const CARTERA = '0c5d0e10-86e3-4d00-8000-000000000003';
export const CUENTA = '0c5d0e10-86e3-4d00-8000-000000000004';
export const PENDIENTE = '0c5d0e10-86e3-4d00-8000-000000000005';
export const RAZON = 'Vidrios y Aluminio del Pacífico SA de CV';
export const FOLIO = 'FA-48213';

export function sql(statement: string): string {
  if (env !== 'test') throw new Error('[86e3qddce] seeding is only allowed in the test environment');
  const url = new URL(process.env.TEST_DATABASE_URL ?? envCfg.db.url);
  if (process.env.TEST_DATABASE_URL && slot) url.pathname += `_s${slot}`; // same rule as scripts/test-env.mjs (slotDbUrl)
  if (!/_s\d+$|millet_test$/.test(url.pathname)) throw new Error(`[86e3qddce] refusing to seed ${url.pathname}`);
  return execFileSync('psql', [url.toString(), '-v', 'ON_ERROR_STOP=1', '-Atq', '-c', statement], {
    encoding: 'utf8',
    env: { ...process.env, PGPASSWORD: decodeURIComponent(url.password) },
  }).trim();
}

export function seed() {
  const emp = sql('select id from compartido.empresas limit 1');
  const mid = sql(`select id from compartido.sucursales where clave='MID'`);
  // one single active issuing branch: without Facturacion:ReppAutomatico:SucursalClave the REP needs exactly one
  sql(`update compartido.sucursales set estatus = 1 where clave <> 'MID'`);
  sql(`
delete from facturacion.repp_pendiente_factura; delete from facturacion.repp_pendiente;
delete from cuentas_por_cobrar.movimiento_cartera; delete from cuentas_por_cobrar.factura_cartera;
delete from tesoreria.cuenta_bancaria where id = '${CUENTA}';
delete from facturacion.factura_venta_linea where factura_venta_id = '${FACTURA}';
delete from facturacion.factura_venta where id = '${FACTURA}'; delete from facturacion.comprobante where id = '${FACTURA}';
delete from compartido.clientes where id = '${CLIENTE}';

insert into compartido.clientes (id, clave, razon_social, rfc, regimen_fiscal, codigo_postal_fiscal, uso_cfdi_default, forma_pago_default, metodo_pago_default,
  moneda_default, es_generico, estatus, origen, bloqueado_por_aw, unificacion_con_historial, version, created_at, updated_at)
values ('${CLIENTE}', 'CLI-0417', '${RAZON}', 'VAP120815KT6', '601', '82000', 'G03', '03', 'PPD', 'MXN', false, 0, 0, false, false, 1, now(), now());

insert into tesoreria.cuenta_bancaria (id, empresa_id, banco, numero_cuenta, clabe, moneda, activa, titular, version, created_at, updated_at)
values ('${CUENTA}', '${emp}', 'BBVA', '0112345678', '012910001123456785', 'MXN', true, 'Vidrios Millet SA de CV', 1, now(), now());

insert into facturacion.comprobante (id, empresa_id, tipo, folio, folio_numero, sucursal_id, receptor_rfc, receptor_nombre, receptor_regimen_fiscal, receptor_codigo_postal,
  receptor_uso_cfdi, receptor_pais, receptor_es_generico, rfc_emisor, regimen_fiscal_emisor, metodo_pago, forma_pago, moneda, subtotal, descuento, impuestos_trasladados,
  retenciones, total, estado, periodo_anio, periodo_mes, uuid, fecha_timbrado, enviado_correo, version, created_at, updated_at, lugar_expedicion, nombre_emisor)
values ('${FACTURA}', '${emp}', 1, '${FOLIO}', 48213, '${mid}', 'VAP120815KT6', '${RAZON}', '601', '82000', 'G03', 'MEX', false, 'VMI100312AB1', '601', 'PPD', '99', 'MXN',
  8620.69, 0, 1379.31, 0, 10000.00, 4, 2026, 10, '7c1e5b9a-3d42-4f6e-9a18-5be0c2d94f31', '2026-10-05 16:20:00+00', false, 1, now(), now(), '97000', 'Vidrios Millet SA de CV');
insert into facturacion.factura_venta (id, comportamiento_fiscal, factura_agrupada) values ('${FACTURA}', 1, false);
insert into facturacion.factura_venta_linea (id, factura_venta_id, posicion, clave_prod_serv_sat, descripcion, clave_unidad_sat, cantidad, valor_unitario, descuento, importe,
  objeto_imp, tasa_iva_traslado, impuesto_trasladado_importe, retencion_total_importe, requiere_pedimento, version, created_at, updated_at)
values (gen_random_uuid(), '${FACTURA}', 1, '30171500', 'Cancel de cristal templado 10 mm, 4 módulos, suministro e instalación', 'H87', 1, 8620.69, 0, 8620.69,
  '02', 0.16, 1379.31, 0, false, 1, now(), now());

insert into cuentas_por_cobrar.factura_cartera (id, empresa_id, factura_venta_id, cliente_id, receptor_rfc, receptor_nombre, uuid, folio, total, moneda, metodo_pago,
  fecha_timbrado, fecha_vencimiento, monto_pagado, monto_nc, estado, version, created_at, updated_at)
values ('${CARTERA}', '${emp}', '${FACTURA}', '${CLIENTE}', 'VAP120815KT6', '${RAZON}', '7c1e5b9a-3d42-4f6e-9a18-5be0c2d94f31', '${FOLIO}', 10000.00, 'MXN', 'PPD',
  '2026-10-05 16:20:00+00', '2026-11-04 06:00:00+00', 0, 0, 1, 1, now(), now());

-- what EmitirReppDesdePagoConfirmadoHandler writes when Tesorería confirms the 4,000 deposit
insert into facturacion.repp_pendiente (id, empresa_id, cliente_id, movimiento_bancario_id, cuenta_bancaria_id, propuesta_id, monto, moneda, fecha_valor, fecha_limite,
  referencia, forma_pago, revisado, estado, version, created_at, updated_at)
values ('${PENDIENTE}', '${emp}', '${CLIENTE}', gen_random_uuid(), '${CUENTA}', gen_random_uuid(), 4000.00, 'MXN', '2026-10-29', '2026-11-05',
  'SPEI 0029847153 · abono factura ${FOLIO}', '03', false, 'Pendiente', 1, now(), now());
insert into facturacion.repp_pendiente_factura (id, repp_pendiente_id, factura_venta_id, importe) values (gen_random_uuid(), '${PENDIENTE}', '${FACTURA}', 4000.00);`);
}
