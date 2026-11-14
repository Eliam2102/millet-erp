/**
 * Validaciones de la demo compra a pago con UNA PERSONA REAL POR ROL (sesiones de Entra en e2e/.auth/test, ver PERSONA en
 * DEMO-COMPRA-PAGO/_demo.ts). Lo que se prueba va por la pantalla y luego por la API; el resto se arma con la API de la propia app
 * (SQL solo para datos maestros que no tienen API: proveedor con CLABE validada, cuenta BBVA de Millet, movimientos de libros).
 *
 *   V-RO-01 rol sin facultad (auditor): no ve Autorizar / Aprobar / Firmar y la API responde 403
 *   V-RO-02 recepción parcial 6 + 4 con su pendiente y factura sobre la parcialidad (86e3tab1k)
 *   V-RO-03 tolerancia factura contra OC en el borde: +0.99 pasa, +1.00 se rechaza (P-015)
 *   V-RO-04 corrida arriba del límite DEMO ($500,000): dos firmas de personas distintas, una del rol «gerente» (T9.1)
 *   V-RO-05 conciliación: diferencia de $1 solo como sugerencia (P-140) y cierre cuadrado con acta
 *   V-RO-06 capturas: proveedor con Bloquear / Poner en revisión en el encabezado y barra superior móvil (390 px) con «Nuevo»
 */
import fs from 'node:fs';
import type { Browser, Page } from '@playwright/test';
import { test, expect, apiCallOn } from '../../_harness/fixtures';
import { asRole, CC_SEED, DEPTO_COMPRAS, MID, rand, today, uuid, warm, SA_EMAIL } from '../86e3pc50v/_data';
import { apiBaseURL } from '../86e3pc50v/_data';
import {
  abrirPeriodoActual, abrirRecepcion, articulo, captureBearer, conceder, crearBinNeutro, CUENTA_MILLET, C_PERMS, CXP_PERMS, COMPRADOR_PERMS,
  lista, ocSinRqTransmitida, PERSONA, PROV, sembrarCuentaMillet, sembrarProveedor, TES_PERMS, umbralesGuion,
} from '../DEMO-COMPRA-PAGO/_demo';
import { capturarSaldoPortal, ok, foto, rechazo } from './_data';
import { extracto, genMovs, movALinea, seedCuenta, sql, type Mov } from './_conciliacion';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(420_000);
test.beforeAll(() => {
  // reuse the supplier / Millet BBVA account if the journey spec already created them (payments reference them)
  if (sql(`select count(*) from compartido.proveedores where id = '${PROV.id}'`) === '0') sembrarProveedor();
  if (sql(`select count(*) from tesoreria.cuenta_bancaria where id = '${CUENTA_MILLET.id}'`) === '0') sembrarCuentaMillet();
});

const idem = () => ({ 'Idempotency-Key': uuid() });
const codigo = async (r: any) => { const t = await r.text(); try { return `${r.status()} ${JSON.parse(t).code ?? ''}`.trim(); } catch { return `${r.status()}`; } };
const r2 = (n: number) => Math.round(n * 100) / 100;
type Rol = Awaited<ReturnType<typeof asRole>>;
/** Read-only cross-branch scopes of the auditor (so he can open the documents of MID); none of them authorizes, approves or signs. */
const AUDITOR_LECTURA = ['compras.requisiciones.leer-todas-sucursales', 'compras.ordenes.leer-todas-sucursales', 'cuentas_por_pagar.documentos.leer-todas-sucursales',
  'cuentas_por_pagar.facturas.leer-todas-sucursales', 'tesoreria.pasivos.ver', 'tesoreria.documentos.leer-todas-sucursales'];

async function abrir(browser: Browser, p: { rol: string; email: string }, url: string, cleanup: any): Promise<Rol> {
  const r = await asRole(browser, p.rol, p.email, url);
  await r.page.setViewportSize({ width: 1600, height: 900 });
  cleanup.add(async () => { await r.context.close(); });
  return r;
}

/** Arrange: OC (sin RQ) del comprador, firmada por B (y N2 por el jefe de almacén si el monto lo pide). */
async function ocAutorizada(o: { guard: any; page: Page; C: Rol; bearerB: { v: string }; articuloId: string; cantidad: number; precio: number; J2?: Rol }) {
  const oc = await ocSinRqTransmitida({ guard: o.guard, page: o.page, A: o.C, bearerB: o.bearerB, provId: PROV.id, articuloId: o.articuloId, cantidad: o.cantidad, precio: o.precio, motivo: 'Compra de insumos de planta' });
  await ok(await o.guard.api.post(`/api/v1/compras/ordenes/${oc.id}/autorizaciones`, { nivel: 1 }));
  let det = await ok(await o.guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  if (det.estado !== 3 && o.J2) {
    await ok(await o.J2.api.post(`/api/v1/compras/ordenes/${oc.id}/autorizaciones`, { nivel: 2 }));
    det = await ok(await o.guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  }
  expect(det.estado, 'OC Autorizada').toBe(3);
  return det as { id: string; folio: string; lineas: { id: string; cantidad: number; precioUnitario: number }[] };
}

/** Arrange (API, CxP): factura del proveedor contra la OC capturada por la persona de CxP. */
async function capturarFactura(api: any, o: { ocId: string; lineaOcId: string; articuloId: string; cantidad: number; precio: number; recepcionId?: string; cfdiRecibidoId?: string; uuidCfdi?: string }) {
  const subtotal = r2(o.cantidad * o.precio); const iva = r2(subtotal * 0.16);
  const enviar = () => api.post('/api/v1/cuentas-por-pagar/facturas', {
    ordenCompraId: o.ocId, proveedorId: PROV.id, sucursalId: MID, cfdiRecibidoId: o.cfdiRecibidoId ?? null, uuidCfdi: o.uuidCfdi ?? uuid().toUpperCase(),
    folioProveedor: String(10000 + Math.floor(Math.random() * 90000)), serieProveedor: 'A', fechaDocumento: today(), fechaContabilizacion: today(), fechaVencimiento: today(),
    moneda: 'MXN', subtotal, descuentos: 0, impuestosTrasladados: iva, retenciones: 0, total: r2(subtotal + iva), recepcionId: o.recepcionId ?? null,
    lineas: [{ articuloId: o.articuloId, claveProdServ: '01010101', descripcion: 'Insumos de planta', cantidad: o.cantidad, claveUnidad: 'H87', precioUnitario: o.precio, importe: subtotal, lineaOcId: o.lineaOcId }],
  }, idem());
  // the reception reaches CxP through its event (Outbox) and Compras records the received quantity a few seconds later: with a
  // reception just made, wait for both instead of failing with RECEPCION_NO_ENCONTRADA / FACTURA_EXCEDE_RECIBIDO (#357)
  const enCarrera = async (r: any) => r.status() === 422 && /RECEPCION_NO_ENCONTRADA|FACTURA_EXCEDE_RECIBIDO/.test(await r.text());
  let res = await enviar();
  for (let i = 0; i < 20 && o.recepcionId && await enCarrera(res); i++) { await new Promise((r) => setTimeout(r, 2000)); res = await enviar(); }
  return ok(res, [200, 201]) as Promise<{ id: string; estado: number; diferenciaContraOc: number; motivoCancelacion: number | null; motivoCancelacionTexto: string | null }>;
}

const recibirApi = async (A: Rol, ocId: string, lineaOcId: string, articuloId: string, cantidad: number, ubicacionId: string) =>
  ok(await A.api.post('/api/v1/almacen/recepciones', {
    ordenCompraId: ocId, fechaMovimiento: today(), cfdiUuidFiscal: uuid().toUpperCase(), observaciones: 'Recepción de la orden',
    lineas: [{ articuloId, lineaOcId, cantidad, ubicacionId }],
  }, idem()), [200, 201]);

test('V-RO-01 rol sin facultad (auditor): no ve Autorizar / Aprobar y la API responde 403', async ({ browser, page, guard, cleanup }) => {
  guard.allowStatus(403);
  const bearerB = captureBearer(page);
  await warm(page, '/compras/requisiciones', SA_EMAIL);
  const A = await abrir(browser, PERSONA.requisitante, '/compras/requisiciones', cleanup);
  const C = await abrir(browser, PERSONA.comprador, '/compras/ordenes', cleanup);
  const AU = await abrir(browser, PERSONA.auditor, '/compras/requisiciones', cleanup);
  await conceder(guard, cleanup, PERSONA.requisitante.email, ['compras.requisiciones.crear', 'compras.requisiciones.leer', 'compras.requisiciones.editar', 'centros_costo.dim3.leer-todos', 'compartido.catalogos.leer', ...CXP_PERMS]);
  await conceder(guard, cleanup, PERSONA.comprador.email, COMPRADOR_PERMS);
  // the auditor reads every branch (read-only scopes); nothing that authorizes, approves or signs
  await conceder(guard, cleanup, PERSONA.auditor.email, AUDITOR_LECTURA);
  await umbralesGuion(guard);
  await abrirPeriodoActual(page, bearerB);
  const art = await articulo(guard, 'Disco de corte para vidrio 4 pulgadas');
  const bin = await crearBinNeutro(guard, art.id);

  // RQ en autorización (capturada por A)
  const rq = await ok(await A.api.post('/api/v1/compras/requisiciones', {
    sucursalId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), departamentoId: DEPTO_COMPRAS, clasificacion: 1, prioridad: 1,
    fechaSolicitud: new Date().toISOString(), descripcion: `Discos de corte ${rand()}`, fechaEntregaDeseada: null, proveedorSugeridoId: null, requisitanteId: null,
  }));
  const rqId = (rq.id ?? rq.requisicionId) as string;
  await ok(await A.api.post(`/api/v1/compras/requisiciones/${rqId}/lineas`, { articuloId: art.id, cantidad: 2, unidadMedida: 'PZA', precioEstimadoMonto: 0, precioEstimadoMoneda: 'MXN', centroCostoId: CC_SEED }));
  await ok(await A.api.post(`/api/v1/compras/requisiciones/${rqId}/transmitir`));
  // OC en autorización (capturada por el comprador) y factura capturada en CxP sobre una OC autorizada
  const ocPend = await ocSinRqTransmitida({ guard, page, A: C, bearerB, provId: PROV.id, articuloId: art.id, cantidad: 2, precio: 120, motivo: 'Reposición de discos de corte' });
  const ocAut = await ocAutorizada({ guard, page, C, bearerB, articuloId: art.id, cantidad: 2, precio: 120 });
  // CxP solo admite la factura por lo recibido (#357): Almacén recibe las 2 piezas antes
  const rec = await recibirApi(A, ocAut.id, ocAut.lineas[0].id, art.id, 2, bin.id);
  const fac = await capturarFactura(A.api, { ocId: ocAut.id, lineaOcId: ocAut.lineas[0].id, articuloId: art.id, cantidad: 2, precio: 120, recepcionId: rec.recepcionId });
  expect(fac.estado, 'factura Capturada').toBe(1);

  const pa = AU.page;
  await pa.goto(`/compras/requisiciones/${rqId}`); await pa.waitForLoadState('networkidle');
  await expect(pa.getByText('En autorización').first()).toBeVisible();
  await expect(pa.getByRole('button', { name: /Aprobar Nivel/ }), 'el auditor no ve «Aprobar Nivel 1/2»').toHaveCount(0);
  await foto(pa, 'V-RO-01a-auditor-requisicion-sin-aprobar');
  expect(await codigo(await AU.api.post(`/api/v1/compras/requisiciones/${rqId}/autorizaciones`, { nivel: 1 }))).toMatch(/^403/);

  await pa.goto(`/compras/ordenes/${ocPend.id}`); await pa.waitForLoadState('networkidle');
  await expect(pa.getByText(ocPend.folio!).first()).toBeVisible();
  await expect(pa.getByRole('button', { name: /Aprobar Nivel|Aprobar$/ }), 'el auditor no ve «Aprobar» de la OC').toHaveCount(0);
  await foto(pa, 'V-RO-01b-auditor-orden-de-compra-sin-aprobar');
  expect(await codigo(await AU.api.post(`/api/v1/compras/ordenes/${ocPend.id}/autorizaciones`, { nivel: 1 }))).toMatch(/^403/);

  await pa.goto(`/cxp/facturas/${fac.id}`); await pa.waitForLoadState('networkidle');
  await expect(pa.getByText('Capturada').first()).toBeVisible();
  await expect(pa.getByRole('button', { name: 'Autorizar', exact: true }), 'el auditor no ve «Autorizar» de la factura').toHaveCount(0);
  await foto(pa, 'V-RO-01c-auditor-factura-sin-autorizar');
  const v = (await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${fac.id}`))).version;
  expect(await codigo(await AU.api.post(`/api/v1/cuentas-por-pagar/facturas/${fac.id}/autorizar`, {}, { 'X-Expected-Version': String(v), ...idem() }))).toMatch(/^403/);
  // la firma de la corrida se prueba con el auditor dentro de V-RO-04 (corrida en autorización)
});

test('V-RO-02 recepción parcial: 6 y luego 4 con su pendiente; factura sobre la parcialidad', async ({ browser, page, guard, cleanup }) => {
  const bearerB = captureBearer(page);
  await warm(page, '/compras/ordenes', SA_EMAIL);
  const A = await abrir(browser, PERSONA.requisitante, '/almacen/recepciones', cleanup);
  const C = await abrir(browser, PERSONA.comprador, '/compras/ordenes', cleanup);
  await conceder(guard, cleanup, PERSONA.requisitante.email, CXP_PERMS);
  await conceder(guard, cleanup, PERSONA.comprador.email, COMPRADOR_PERMS);
  await umbralesGuion(guard);
  await abrirPeriodoActual(page, bearerB);
  const art = await articulo(guard, 'Cinta doble cara para vidrio 19 mm');
  const bin = await crearBinNeutro(guard, art.id);
  const oc = await ocAutorizada({ guard, page, C, bearerB, articuloId: art.id, cantidad: 10, precio: 85 });
  const pa = A.page;

  const recibir = async (cantidad: number) => {
    const dlg = await abrirRecepcion(pa, oc.folio);
    await dlg.getByRole('combobox', { name: 'Seleccionar ubicación' }).last().click();
    await pa.getByRole('option', { name: new RegExp(bin.clave) }).click();
    await dlg.getByRole('button', { name: /Captura el folio fiscal/ }).click();
    await dlg.getByLabel(/Folio fiscal/).fill(uuid().toUpperCase());
    await dlg.getByLabel('Cantidad a recibir, partida 1').fill(String(cantidad));
    const res = await apiCallOn(pa, () => dlg.getByRole('button', { name: 'Registrar recepción' }).click(), { method: 'POST', url: /\/almacen\/recepciones$/, status: 201 });
    return (await res.json()) as { recepcionId: string; folio: string };
  };
  const rec1 = await recibir(6);
  await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`))).lineas[0].cantidadRecibida, { timeout: 40_000 }).toBe(6);
  const det1 = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  expect(det1.subEstadoRecepcion, 'OC con recepción parcial (SubEstadoRecepcion.Parcial)').toBe(1);
  await page.goto(`/compras/ordenes/${oc.id}`); await page.waitForLoadState('networkidle');
  await expect(page.getByText('Recepción parcial').first()).toBeVisible();
  await page.getByRole('tab', { name: 'Partidas' }).click();
  await expect(page.getByRole('columnheader', { name: 'Por surtir' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: art.clave }).first()).toContainText('4');
  await foto(page, 'V-RO-02a-oc-recepcion-parcial-6-de-10-pendiente-4');

  // factura del proveedor por lo recibido (6) ligada a la recepción parcial
  const fac = await capturarFactura(A.api, { ocId: oc.id, lineaOcId: oc.lineas[0].id, articuloId: art.id, cantidad: 6, precio: 85, recepcionId: rec1.recepcionId });
  expect(fac.estado, 'factura sobre la parcialidad Capturada, sin diferencia').toBe(1);
  expect(fac.diferenciaContraOc).toBe(0);
  await page.goto(`/cxp/facturas/${fac.id}`); await page.waitForLoadState('networkidle');
  await expect(page.getByText(rec1.folio).first()).toBeVisible();
  await foto(page, 'V-RO-02b-factura-sobre-la-recepcion-parcial');

  await recibir(4);
  await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`))).subEstadoRecepcion, { timeout: 40_000 }).toBe(2);
  await page.goto(`/compras/ordenes/${oc.id}`); await page.waitForLoadState('networkidle');
  await expect(page.getByText('Recepción completa').first()).toBeVisible();
  await foto(page, 'V-RO-02c-oc-recepcion-completa-10-de-10');
  // un recibo más ya no cabe (Almacén recibe exacto)
  guard.allowStatus(422);
  const extra = await A.api.post('/api/v1/almacen/recepciones', { ordenCompraId: oc.id, fechaMovimiento: today(), cfdiUuidFiscal: uuid().toUpperCase(), observaciones: 'x', lineas: [{ articuloId: art.id, lineaOcId: oc.lineas[0].id, cantidad: 1, ubicacionId: bin.id }] }, idem());
  expect(await codigo(extra)).toMatch(/RECEPCION_EXCEDE_PENDIENTE/);
});

test('V-RO-03 tolerancia factura contra OC en el borde: +0.99 pasa y +1.00 se rechaza (P-015)', async ({ browser, page, guard, cleanup }) => {
  const bearerB = captureBearer(page);
  await warm(page, '/cxp/facturas', SA_EMAIL);
  const A = await abrir(browser, PERSONA.requisitante, '/cxp/facturas', cleanup);
  const C = await abrir(browser, PERSONA.comprador, '/compras/ordenes', cleanup);
  await conceder(guard, cleanup, PERSONA.requisitante.email, CXP_PERMS);
  await conceder(guard, cleanup, PERSONA.comprador.email, COMPRADOR_PERMS);
  await umbralesGuion(guard);
  await abrirPeriodoActual(page, bearerB);
  const art = await articulo(guard, 'Silicón estructural cartucho 300 ml');
  const bin = await crearBinNeutro(guard, art.id);
  const casos: Array<[string, number, number]> = [['099', 0.99, 1], ['100', 1.0, 5]];
  for (const [n, extra, esperado] of casos) {
    const oc = await ocAutorizada({ guard, page, C, bearerB, articuloId: art.id, cantidad: 1, precio: 950 });
    // lo recibido vale 950: +0.99 queda dentro de la tolerancia de dinero; +1.00 se cancela por tolerancia contra la OC
    const rec = await recibirApi(A, oc.id, oc.lineas[0].id, art.id, 1, bin.id);
    const fac = await capturarFactura(A.api, { ocId: oc.id, lineaOcId: oc.lineas[0].id, articuloId: art.id, cantidad: 1, precio: r2(950 + extra), recepcionId: rec.recepcionId });
    test.info().annotations.push({ type: `tolerancia +${extra.toFixed(2)}`, description: `estado=${fac.estado} diferencia=${fac.diferenciaContraOc} motivo=${fac.motivoCancelacion} ${fac.motivoCancelacionTexto ?? ''}` });
    expect(fac.estado, `+${extra.toFixed(2)} sobre el precio de la OC`).toBe(esperado);
    if (esperado === 5) expect(fac.motivoCancelacion, 'cancelada por tolerancia').toBe(1);
    await page.goto(`/cxp/facturas/${fac.id}`); await page.waitForLoadState('networkidle');
    await expect(page.getByText(esperado === 1 ? 'Capturada' : 'Rechazada por tolerancia').first()).toBeVisible();
    await foto(page, `V-RO-03${n === '099' ? 'a' : 'b'}-factura-mas-${n}-${esperado === 1 ? 'pasa' : 'rechazada-por-tolerancia'}`);
  }
});

test('V-RO-04 corrida arriba del límite DEMO ($500,000): dos firmas de personas distintas, una del rol «gerente» (T9.1)', async ({ browser, page, guard, cleanup }) => {
  guard.allowStatus(403, 422);
  const bearerB = captureBearer(page);
  await warm(page, '/tesoreria/corridas', SA_EMAIL);
  const A = await abrir(browser, PERSONA.requisitante, '/tesoreria/corridas', cleanup);
  const C = await abrir(browser, PERSONA.comprador, '/compras/ordenes', cleanup);
  const J2 = await abrir(browser, PERSONA.jefeAlmacen, '/tesoreria/corridas', cleanup);
  const AU = await abrir(browser, PERSONA.auditor, '/tesoreria/corridas', cleanup);
  const FIRMA = ['tesoreria.corridas.autorizar', 'tesoreria.pasivos.ver', 'tesoreria.cuentas.ver', 'tesoreria.documentos.leer-todas-sucursales', 'cuentas_por_pagar.facturas.leer'];
  // política de firmas vigente (escalón DEMO de la ficha 86e3ke5bt) y rol «gerente» del segundo firmante (sembrado por Identidad al
  // arrancar el API, #358; aquí solo se asigna a la persona).
  // Va ANTES de conceder permisos: un cambio de rol borra las excepciones de permisos del usuario (ADR-0053).
  const politica = await ok(await guard.api.get('/api/v1/tesoreria/corridas/politica-firmas'));
  test.info().annotations.push({ type: 'politica-firmas', description: JSON.stringify(politica) });
  const me = await ok(await guard.api.get('/api/auth/me'));
  const roles = lista(await ok(await guard.api.get('/api/v1/identidad/roles?limit=200')));
  const gerente = roles.find((r: { codigo: string }) => r.codigo === 'gerente');
  expect(gerente, 'Identidad siembra el rol «gerente»').toBeTruthy();
  const users = await ok(await guard.api.get('/api/v1/identidad/usuarios?limit=100'));
  const oscarId = users.items.find((u: { email: string }) => u.email === PERSONA.jefeAlmacen.email).id;
  const asig = await guard.api.post(`/api/v1/identidad/usuarios/${oscarId}/asignaciones`, { empresaId: me.currentEmpresaId, rolId: gerente.id }, idem());
  expect([201, 409], `asignar rol gerente: ${asig.status()} ${(await asig.text()).slice(0, 200)}`).toContain(asig.status());
  if (asig.status() === 201) { const a = await asig.json(); cleanup.add(async () => { await guard.api.delete(`/api/v1/identidad/usuarios/asignaciones/${a.id}`, undefined, idem()); }); }

  await conceder(guard, cleanup, PERSONA.requisitante.email, [...CXP_PERMS, ...TES_PERMS]);
  await conceder(guard, cleanup, PERSONA.comprador.email, COMPRADOR_PERMS);
  await conceder(guard, cleanup, PERSONA.jefeAlmacen.email, [...C_PERMS, ...FIRMA]);
  await conceder(guard, cleanup, PERSONA.auditor.email, AUDITOR_LECTURA);
  await umbralesGuion(guard);
  await abrirPeriodoActual(page, bearerB);

  // pasivo real de $603,200: OC (N1 B + N2 jefe de almacén) -> recepción (A) -> CFDI -> factura (A) -> autorizada (B) -> Tesorería
  const PRECIO = 520_000;
  const art = await articulo(guard, 'Horno de templado: refacción de rodillos cerámicos');
  const bin = await crearBinNeutro(guard, art.id);
  const oc = await ocAutorizada({ guard, page, C, bearerB, articuloId: art.id, cantidad: 1, precio: PRECIO, J2 });
  const rec = await recibirApi(A, oc.id, oc.lineas[0].id, art.id, 1, bin.id);
  const uuidCfdi = uuid().toUpperCase();
  const iva = r2(PRECIO * 0.16); const total = r2(PRECIO + iva);
  const xml = `<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" TipoDeComprobante="I" Serie="A" Folio="${rand()}" Fecha="${today()}T10:00:00" Total="${total.toFixed(2)}" SubTotal="${PRECIO.toFixed(2)}" Moneda="MXN" MetodoPago="PUE"><cfdi:Emisor Rfc="${PROV.rfc}" Nombre="${PROV.razon}" /><cfdi:Receptor Rfc="MIL010101AAA" Nombre="Millet SA" /><cfdi:Conceptos><cfdi:Concepto ClaveProdServ="01010101" Cantidad="1" ClaveUnidad="H87" Descripcion="Rodillos ceramicos" ValorUnitario="${PRECIO.toFixed(2)}" Importe="${PRECIO.toFixed(2)}" /></cfdi:Conceptos><cfdi:Impuestos TotalImpuestosTrasladados="${iva.toFixed(2)}" /><cfdi:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="${uuidCfdi}" FechaTimbrado="${today()}T10:00:00" /></cfdi:Complemento></cfdi:Comprobante>`;
  const up = await page.request.fetch(`${apiBaseURL}/api/v1/cuentas-por-pagar/cfdis/cargar`, { method: 'POST', headers: { authorization: bearerB.v, ...idem() }, multipart: { xml: { name: 'factura.xml', mimeType: 'text/xml', buffer: Buffer.from(xml) } } });
  const cfdi = await ok(up, [200, 201]);
  const fac = await capturarFactura(A.api, { ocId: oc.id, lineaOcId: oc.lineas[0].id, articuloId: art.id, cantidad: 1, precio: PRECIO, recepcionId: rec.recepcionId, cfdiRecibidoId: cfdi.id, uuidCfdi });
  expect(fac.estado).toBe(1);
  const vf = (await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${fac.id}`))).version;
  await ok(await guard.api.post(`/api/v1/cuentas-por-pagar/facturas/${fac.id}/autorizar`, {}, { 'X-Expected-Version': String(vf), ...idem() }));
  await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/tesoreria/pasivos-pendientes?proveedorId=${PROV.id}`))).items.some((p: { facturaProveedorId: string }) => p.facturaProveedorId === fac.id),
    { timeout: 40_000, message: 'Tesorería recibe el pasivo autorizado' }).toBe(true);

  // A arma la corrida solo con esta factura
  const pa = A.page;
  await pa.goto('/tesoreria/corridas/nueva'); await pa.waitForLoadState('networkidle');
  await pa.getByRole('combobox', { name: 'Cuenta bancaria' }).click();
  await pa.getByRole('option').filter({ hasText: CUENTA_MILLET.banco }).filter({ hasText: CUENTA_MILLET.numero.slice(-4) }).first().click();
  const filaFac = pa.getByRole('checkbox', { name: new RegExp(`${fac.id}|${PRECIO.toLocaleString('en-US')}|603,200`) });
  const cbProv = pa.getByRole('checkbox', { name: `Elegir todas las facturas de ${PROV.razon}` });
  await expect(cbProv).toBeVisible({ timeout: 20_000 });
  await pa.getByLabel('Nombre (opcional)').fill('Pago de refacciones del horno de templado');
  if (await filaFac.count() === 1) await filaFac.check(); else await cbProv.check();
  await pa.getByRole('button', { name: 'Crear corrida en borrador' }).click();
  await pa.waitForURL(/\/tesoreria\/corridas\/[0-9a-f-]{36}$/);
  const corridaId = pa.url().split('/').pop()!;
  await pa.getByRole('button', { name: 'Enviar a autorización' }).click();
  await expect(pa.getByText('En autorización', { exact: true }).first()).toBeVisible();
  const c0 = await ok(await guard.api.get(`/api/v1/tesoreria/corridas/${corridaId}`));
  test.info().annotations.push({ type: 'corrida', description: `total=${c0.total} firmasRequeridas=${c0.firmasRequeridas} rol=${c0.rolRequerido}` });
  expect(c0.total, 'corrida arriba del límite DEMO').toBeGreaterThanOrEqual(500_000);
  expect(c0.firmasRequeridas, 'T9.1: arriba de $500,000 se piden dos firmas').toBe(2);
  await foto(pa, 'V-RO-04a-corrida-arriba-del-limite-pide-dos-firmas');

  // primera firma: B (sin rol gerente) -> sigue en autorización
  await page.goto(`/tesoreria/corridas/${corridaId}`); await page.waitForLoadState('networkidle');
  await capturarSaldoPortal(page);
  await page.getByRole('button', { name: 'Firmar y autorizar' }).click();
  const dlg = page.getByRole('alertdialog').or(page.getByRole('dialog')).filter({ hasText: 'Firmar la corrida' });
  await dlg.getByRole('button', { name: 'Firmar y autorizar' }).click();
  await expect(page.getByText('Firma registrada').first()).toBeVisible();
  await expect(page.getByText('En autorización', { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/1 de 2/).first()).toBeVisible();
  await foto(page, 'V-RO-04b-primera-firma-falta-la-del-gerente');
  expect(await codigo(await guard.api.post(`/api/v1/tesoreria/corridas/${corridaId}/autorizar`, {}, { 'X-Expected-Version': String((await ok(await guard.api.get(`/api/v1/tesoreria/corridas/${corridaId}`))).version), ...idem() })), 'la misma persona no da las dos firmas').toBe('422 CORRIDA_YA_FIRMADA');
  // con una firma dada el importe ya no cambia: quitar el pasivo se rechaza (no hay firma que invalidar porque no se puede editar)
  const cv = (await ok(await guard.api.get(`/api/v1/tesoreria/corridas/${corridaId}`))).version;
  const quitar = await A.api.delete(`/api/v1/tesoreria/corridas/${corridaId}/pasivos/${fac.id}`, undefined, { 'X-Expected-Version': String(cv), ...idem() });
  const qc = await codigo(quitar);
  test.info().annotations.push({ type: 'cambiar importe tras firmar', description: qc });
  expect(qc, 'el importe de una corrida en autorización no se puede cambiar').toMatch(/^4\d\d CORRIDA_ESTADO_INVALIDO/);

  // el auditor (sin facultad) no ve «Firmar y autorizar» y la API responde 403
  await AU.page.goto(`/tesoreria/corridas/${corridaId}`); await AU.page.waitForLoadState('networkidle');
  await expect(AU.page.getByRole('button', { name: 'Firmar y autorizar' })).toHaveCount(0);
  await foto(AU.page, 'V-RO-01d-auditor-corrida-sin-firmar');
  expect(await codigo(await AU.api.post(`/api/v1/tesoreria/corridas/${corridaId}/autorizar`, {}, { 'X-Expected-Version': String(cv), ...idem() }))).toMatch(/^403/);

  // segunda firma: jefe de almacén con rol «gerente» -> Autorizada con dos firmantes distintos
  await J2.page.goto(`/tesoreria/corridas/${corridaId}`); await J2.page.waitForLoadState('networkidle');
  await capturarSaldoPortal(J2.page);
  await J2.page.getByRole('button', { name: 'Firmar y autorizar' }).click();
  const dlg2 = J2.page.getByRole('alertdialog').or(J2.page.getByRole('dialog')).filter({ hasText: 'Firmar la corrida' });
  await dlg2.getByRole('button', { name: 'Firmar y autorizar' }).click();
  // the run state is serialized by name («Autorizada») in this endpoint
  const fin = await expect.poll(async () => String((await ok(await guard.api.get(`/api/v1/tesoreria/corridas/${corridaId}`))).estado), { timeout: 15_000 }).toMatch(/^(3|Autorizada)$/).then(() => true, () => false);
  const c2 = await ok(await guard.api.get(`/api/v1/tesoreria/corridas/${corridaId}`));
  test.info().annotations.push({ type: 'firmas', description: JSON.stringify(c2.firmas) });
  expect.soft(fin, 'DEFECTO: con dos firmas distintas (una del rol «gerente») la corrida debe quedar Autorizada').toBe(true);
  expect(new Set(c2.firmas.map((f: { usuarioId: string }) => f.usuarioId)).size).toBe(2);
  await J2.page.reload(); await J2.page.waitForLoadState('networkidle');
  await expect(J2.page.getByText('Autorizada', { exact: true }).first()).toBeVisible();
  for (const f of c2.firmas as { usuarioNombre: string }[]) await expect(J2.page.getByText(f.usuarioNombre).first()).toBeVisible();
  await foto(J2.page, 'V-RO-04c-segunda-firma-gerente-corrida-autorizada');
});

test('V-RO-05 conciliación: diferencia de $1 solo como sugerencia (P-140) y cierre cuadrado con acta', async ({ page, guard }) => {
  guard.allowStatus(422);
  const n = 7 + Math.floor(Math.random() * 900); // cuentas nuevas por corrida (las de V-CO usan 5 y 6)
  const numero = (k: number) => `0119${String(k).padStart(6, '0')}`;
  const dif: Mov = { dia: 15, desc: 'PAGO SPEI HERRAJES INDUSTRIALES DEL NORTE', ref: 'SPEI2609150001', sentido: 2, monto: 18_450.0, concepto: '0000000b-1001-0000-0000-000000000001' };
  const base = genMovs(3, 700 + n);
  const c1 = seedCuenta(1000 + n, numero(n), [...base, dif]);
  // el banco cargó $1.00 más y con otra referencia: no empareja por referencia ni por importe exacto
  const ext1 = extracto([...base.map(movALinea), { dia: 15, desc: dif.desc, ref: 'BNET0099812', cargo: dif.monto + 1, abono: null }]);
  const base2 = genMovs(4, 800 + n);
  const c2 = seedCuenta(2000 + n, numero(n + 500000), base2);
  const ext2 = extracto(base2.map(movALinea));

  const abrirEImportar = async (cuenta: string, texto: string) => {
    await page.goto('/tesoreria/conciliacion');
    await expect(page.getByRole('heading', { name: 'Conciliación bancaria' })).toBeVisible();
    const combo = (i: number) => page.getByText(['Cuenta Bancaria', 'Año', 'Mes'][i], { exact: true }).first().locator('xpath=following::button[@role="combobox"][1]');
    await combo(0).click(); await page.getByRole('option', { name: new RegExp(cuenta) }).click();
    await combo(1).click(); await page.getByRole('option', { name: '2026', exact: true }).click();
    await combo(2).click(); await page.getByRole('option', { name: 'Septiembre' }).click();
    await page.getByRole('button', { name: 'Nueva conciliación' }).click();
    await page.getByRole('button', { name: 'Abrir Conciliación' }).click();
    await expect(page.getByText('Conciliación abierta con éxito').first()).toBeVisible();
    await page.getByRole('button', { name: 'Cargar extracto', exact: true }).click();
    const dlg = page.getByRole('dialog');
    await dlg.getByRole('textbox').last().fill(texto);
    await apiCallOn(page, () => dlg.getByRole('button', { name: 'Cargar y Procesar' }).click(), { method: 'POST', url: /conciliaciones\/[^/]+\/extracto$/, status: [200, 201] });
    await expect(dlg).toBeHidden();
  };
  const idDe = (numeroCuenta: string) => sql(`select c.id from tesoreria.conciliacion c join tesoreria.cuenta_bancaria b on b.id = c.cuenta_bancaria_id where b.numero_cuenta = '${numeroCuenta}'`);

  await abrirEImportar(c1.numero, ext1.texto);
  const d1 = await ok(await guard.api.get(`/api/v1/tesoreria/conciliaciones/${idDe(c1.numero)}`));
  const linea = d1.lineas.find((l: { referencia?: string; descripcion?: string }) => (l.referencia ?? '').includes('BNET0099812') || (l.descripcion ?? '').includes('HERRAJES'));
  test.info().annotations.push({ type: 'linea +$1', description: `estado=${linea?.estado} matchTipo=${linea?.matchTipo}` });
  const fila = page.getByRole('row').filter({ hasText: 'BNET0099812' }).first();
  await fila.scrollIntoViewIfNeeded();
  await foto(page, 'V-RO-05a-diferencia-de-1-peso-formato-DEMO');
  expect.soft(linea?.estado, 'DEFECTO P-140: una diferencia de $1 debe quedar como sugerencia (En revisión), no emparejarse sola').not.toBe(2);

  // cierre cuadrado: todo emparejado y diferencia $0.00 -> acta firmada por la Gerencia de Tesorería
  await abrirEImportar(c2.numero, ext2.texto);
  await expect(page.getByText(`${base2.length} de ${base2.length} líneas emparejadas`)).toBeVisible();
  const id2 = idDe(c2.numero);
  const d2 = await ok(await guard.api.get(`/api/v1/tesoreria/conciliaciones/${id2}`));
  test.info().annotations.push({ type: 'cierre', description: `diferencia=${d2.diferencia}` });
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.getByRole('button', { name: 'Cerrar con Acta' }).click();
  const dlg = page.getByRole('dialog').filter({ hasText: 'Cierre cuadrado de conciliación' });
  await expect(dlg).toBeVisible();
  await dlg.getByRole('textbox').first().fill('Marisol Treviño Garza');
  const puesto = dlg.getByRole('combobox').first();
  if (await puesto.count()) { await puesto.click(); await page.getByRole('option', { name: 'Gerente de Tesorería' }).click(); }
  await foto(page, 'V-RO-05b-cierre-cuadrado-firmante');
  const descarga = page.waitForEvent('download');
  await apiCallOn(page, () => dlg.getByRole('button', { name: 'Firmar, Cerrar y Descargar Acta' }).click(), { method: 'POST', url: new RegExp(`conciliaciones/${id2}/cerrar$`), status: 200 });
  const pdf = fs.readFileSync(await (await descarga).path());
  expect(pdf.subarray(0, 4).toString(), 'el acta es un PDF').toBe('%PDF');
  await expect(page.getByText(/Conciliación cerrada con éxito/).first()).toBeVisible();
  expect((await ok(await guard.api.get(`/api/v1/tesoreria/conciliaciones/${id2}`))).estado, 'Cerrada').toBe(2);
  await foto(page, 'V-RO-05c-conciliacion-cerrada-con-acta');
});

test('V-RO-06 capturas: proveedor con acciones en el encabezado y barra superior móvil (390 px) con «Nuevo»', async ({ page }) => {
  await warm(page, `/admin/datos-maestros/proveedores/${PROV.id}`, SA_EMAIL);
  await expect(page.getByRole('heading', { name: PROV.clave }).first()).toBeVisible();
  const bloquear = page.getByRole('button', { name: 'Bloquear proveedor' });
  const revision = page.getByRole('button', { name: 'Poner en revisión' });
  await expect(bloquear).toBeVisible();
  await expect(revision).toBeVisible();
  const yB = (await bloquear.boundingBox())!.y; const yR = (await revision.boundingBox())!.y;
  expect(Math.max(yB, yR), 'ambas acciones en el encabezado (parte superior)').toBeLessThan(300);
  await foto(page, 'V-RO-06a-proveedor-bloquear-y-poner-en-revision-en-encabezado');

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/compras/requisiciones'); await page.waitForLoadState('networkidle');
  const nuevo = page.getByRole('button', { name: 'Crear nuevo' });
  await expect(nuevo).toBeVisible();
  const box = (await nuevo.boundingBox())!;
  expect(box.x + box.width, '«Nuevo» dentro de los 390 px').toBeLessThanOrEqual(390);
  test.info().annotations.push({ type: 'nuevo-movil', description: `x=${box.x} w=${box.width} texto=${JSON.stringify(await nuevo.innerText())}` });
  await foto(page, 'V-RO-06b-barra-superior-movil-390-con-nuevo');
});
