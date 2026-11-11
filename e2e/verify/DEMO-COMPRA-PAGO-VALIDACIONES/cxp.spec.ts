/**
 * Validaciones de CUENTAS POR PAGAR: la factura (CFDI) se carga y captura contra una OC por la pantalla real. Cada caso hace
 * lo indebido en la pantalla y luego repite la operación contra la API para afirmar el rechazo del servidor.
 * La OC se deja autorizada por SQL (la firma de otra persona no es lo que se prueba aquí). Desde #357 CxP rechaza la factura contra
 * OC que excede lo recibido sin facturar (FACTURA_EXCEDE_RECIBIDO), así que los casos que deben capturar reciben la OC antes.
 */
import { SA_EMAIL, warm } from '../86e3pc50v/_data';
import type { Page } from '@playwright/test';
import { test, expect, shot, type Guard } from '../../_harness/fixtures';
import { apiBaseURL } from '../../_harness/session';
import {
  abrirContabilidad, captureBearer, crearArticulo, crearBin, CC_SEED, DEPTO_COMPRAS, MID, nombrarCatalogos, ok, PROV_VIDRIO, rand, rechazo, sql, today, uuid, foto } from './_data';
import { habilitarProveedorParaPago, sembrarCuentaMillet } from './_tesoreria';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(240_000);
// warm() completes the SSO sign-in if the saved session needs it and waits for the workspace, so guard.api has the page's token
test.beforeEach(async ({ page }) => { await warm(page, '/cxp/facturas', SA_EMAIL); });

const CANT = 20; const PRECIO = 850;
const SUBTOTAL = CANT * PRECIO; const IVA = Math.round(SUBTOTAL * 0.16 * 100) / 100;
const RFC_PROV = 'SES010101AAA'; // PROV-EST (seed), renombrado como Vidrio Flotado del Bajío
const RFC_AJENO = 'VTE180920AB1';
const S: { ocId?: string; ocFolio?: string; lineaId?: string; articuloId?: string; binId?: string; yo?: string; base?: { facturaId: string; uuid: string; folio: string; body: any; cfdiXml: string }; bearer?: { v: string } } = {};

const settle = async (p: Page) => { await p.waitForLoadState('networkidle'); await p.waitForTimeout(700); };
const toast = (p: Page) => p.locator('[data-sonner-toast]').first();

function cfdiXml(o: { uuid: string; rfc: string; fecha: string; folio: string; subtotal?: number; precio?: number }) {
  const subtotal = o.subtotal ?? SUBTOTAL; const iva = Math.round(subtotal * 0.16 * 100) / 100;
  return `<?xml version="1.0" encoding="UTF-8"?>
<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" TipoDeComprobante="I" Serie="F" Folio="${o.folio}" Fecha="${o.fecha}T10:00:00" Total="${(subtotal + iva).toFixed(2)}" SubTotal="${subtotal.toFixed(2)}" Moneda="MXN" MetodoPago="PUE">
  <cfdi:Emisor Rfc="${o.rfc}" Nombre="Vidrio Flotado del Bajío S.A. de C.V." />
  <cfdi:Receptor Rfc="VMI0305129A4" Nombre="Vidrios Millet S.A. de C.V." />
  <cfdi:Conceptos><cfdi:Concepto ClaveProdServ="30161800" Cantidad="${CANT}" ClaveUnidad="H87" Descripcion="Vidrio flotado claro 6 mm hoja 2.44 x 3.60 m" ValorUnitario="${(subtotal / CANT).toFixed(2)}" Importe="${subtotal.toFixed(2)}" /></cfdi:Conceptos>
  <cfdi:Impuestos TotalImpuestosTrasladados="${iva.toFixed(2)}" />
  <cfdi:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="${o.uuid}" FechaTimbrado="${o.fecha}T10:00:00" /></cfdi:Complemento>
</cfdi:Comprobante>`;
}
const nuevoUuid = () => uuid().toUpperCase();
const numFolio = () => String(Math.floor(10000 + Math.random() * 89999));

async function cargarCfdi(page: Page, xml: string, nombre: string) {
  await page.goto('/cxp/cfdis');
  await settle(page);
  await page.getByRole('button', { name: 'Cargar CFDI' }).first().click();
  const sheet = page.getByRole('dialog');
  await sheet.locator('#cfdi-xml').setInputFiles({ name: nombre, mimeType: 'text/xml', buffer: Buffer.from(xml) });
  await expect(sheet.getByRole('button', { name: 'Cargar CFDI' })).toBeEnabled();
  const res = page.waitForResponse((r) => r.url().endsWith('/cfdis/cargar') && r.request().method() === 'POST');
  await sheet.getByRole('button', { name: 'Cargar CFDI' }).click();
  return { sheet, res: await res };
}

/** Abre «Capturar factura» desde la fila del CFDI, elige la OC, importa la línea del CFDI y la liga a la línea de la OC. */
async function abrirCaptura(page: Page, uuidCfdi: string, ocFolio?: string) {
  const folioOc = ocFolio ?? S.ocFolio!;
  await page.goto('/cxp/cfdis');
  await settle(page);
  await page.getByLabel('Buscar por UUID o folio').fill(uuidCfdi);
  await page.getByRole('button', { name: new RegExp(`Acciones para CFDI ${uuidCfdi}`, 'i') }).click();
  await page.getByRole('menuitem', { name: 'Capturar factura' }).click();
  const cap = page.getByRole('dialog', { name: 'Capturar factura desde OC' });
  await expect(cap).toBeVisible();
  await cap.getByRole('combobox', { name: 'Seleccionar orden de compra' }).click();
  await page.getByPlaceholder('Folio o referencia…').fill(folioOc);
  await page.getByRole('option', { name: new RegExp(folioOc) }).click();
  await cap.getByRole('button', { name: /Importar 1 partida/ }).click();
  await cap.getByRole('combobox', { name: 'Seleccionar línea de OC' }).click();
  await page.getByRole('option').first().click();
  return cap;
}

/** OC autorizada (arrange por SQL) con 20 piezas de vidrio a $850. Devuelve folio y línea. */
async function crearOc(guard: Guard) {
  const art = await crearArticulo(guard, { clave: `VID-FLT-06-${rand()}`, nombre: 'Vidrio flotado claro 6 mm hoja 2.44 x 3.60 m' });
  const bin = await crearBin(guard, [art.id]);
  const oc = await ok(await guard.api.post('/api/v1/compras/ordenes', {
    sucursalDestinoId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), proveedorId: PROV_VIDRIO,
    condicionesPagoId: uuid(), usoPrincipalId: uuid(), fechaDocumento: today(), moneda: 'MXN',
    sinRequisicionPrevia: true, motivoSinRequisicion: 'Reposición de vidrio flotado', observaciones: 'Reposición de vidrio flotado',
  }));
  await ok(await guard.api.post(`/api/v1/compras/ordenes/${oc.id}/lineas`, {
    articuloId: art.id, cantidad: CANT, unidadMedida: 'PZA', precioUnitario: PRECIO, departamentoSolicitanteId: DEPTO_COMPRAS, centroCostoId: CC_SEED,
  }));
  const det = await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.id}`));
  sql(`update compras.ordenes_compra set estado=3, fecha_contabilizacion=now() where id='${oc.id}'`);
  return { ocId: oc.id as string, ocFolio: det.folio as string, lineaId: det.lineas[0].id as string, articuloId: art.id, binId: bin.id };
}

/** Arrange (API de Almacén): recibe las 20 piezas de la OC y espera a que Compras registre lo recibido (llega por su evento). */
async function recibirOc(guard: Guard, oc: { ocId: string; lineaId: string; articuloId: string; binId: string }) {
  await ok(await guard.api.post('/api/v1/almacen/recepciones', {
    ordenCompraId: oc.ocId, fechaMovimiento: today(), cfdiUuidFiscal: nuevoUuid(), observaciones: 'Recepción de vidrio flotado',
    lineas: [{ articuloId: oc.articuloId, lineaOcId: oc.lineaId, cantidad: CANT, ubicacionId: oc.binId }],
  }, { 'Idempotency-Key': uuid() }), [200, 201]);
  await expect.poll(async () => (await ok(await guard.api.get(`/api/v1/compras/ordenes/${oc.ocId}`))).lineas[0].cantidadRecibida,
    { timeout: 40_000, message: 'Compras registra lo recibido' }).toBe(CANT);
}

/** Catálogos, periodos y la OC base (una vez por worker). */
async function prepOc(guard: Guard) {
  if (S.ocId) return;
  nombrarCatalogos();
  await abrirContabilidad(guard);
  S.yo = (await ok(await guard.api.get('/api/auth/me'))).userId;
  Object.assign(S, await crearOc(guard));
  await recibirOc(guard, S as Required<typeof S>);
  sembrarCuentaMillet();
  habilitarProveedorParaPago(PROV_VIDRIO, 1);
}

/** Factura dentro de tolerancia capturada por la pantalla: base de los casos de UUID repetido y de pago. */
async function prepBase(page: Page, guard: Guard) {
  await prepOc(guard);
  if (S.base) return;
  const u = nuevoUuid(); const num = numFolio(); const folio = `F-${num}`;
  const xml = cfdiXml({ uuid: u, rfc: RFC_PROV, fecha: today(), folio: num });
  const { res } = await cargarCfdi(page, xml, 'factura-vidrio-flotado.xml');
  expect(res.status()).toBeLessThan(300);
  const cap = await abrirCaptura(page, u);
  const post = page.waitForResponse((r) => /\/cuentas-por-pagar\/facturas$/.test(r.url()) && r.request().method() === 'POST');
  await cap.getByRole('button', { name: 'Capturar factura' }).click();
  const r = await post;
  expect(r.status()).toBe(201);
  const out = await r.json();
  expect(out.estado, 'Capturada').toBe(1);
  S.base = { facturaId: out.id, uuid: u, folio, body: JSON.parse(r.request().postData() ?? '{}'), cfdiXml: xml };
}

test('V-CXP-01 UUID repetido: el CFDI y la factura no se registran dos veces', async ({ page, guard }) => {
  await prepBase(page, guard);
  const b = S.base!;
  // Pantalla: se vuelve a cargar el mismo XML
  guard.allowStatus(422);
  guard.expectApiMutationError(422);
  const { res } = await cargarCfdi(page, b.cfdiXml, 'factura-vidrio-flotado.xml');
  expect(res.status()).toBe(422);
  await page.waitForTimeout(800);
  await foto(page, 'V-CXP-01-cfdi-con-uuid-repetido-se-rechaza');
  const prob = JSON.parse(await res.text());
  expect(prob.code ?? JSON.stringify(prob)).toContain('CFDI_DUPLICADO');

  // Servidor: la misma factura (mismo UUID) otra vez
  const dup = await rechazo(await guard.api.post('/api/v1/cuentas-por-pagar/facturas', { ...b.body, cfdiRecibidoId: null }, { 'Idempotency-Key': uuid() }), 422, 'FACTURA_DUPLICADA');
  expect(dup.detail).toContain(b.uuid);
  expect(sql(`select count(*) from cuentas_por_pagar.facturas_proveedor where uuid_cfdi = '${b.uuid}'`), 'una sola factura con ese UUID').toBe('1');
});

test('V-CXP-02 CFDI de un RFC distinto al del proveedor de la OC no se captura', async ({ page, guard }) => {
  await prepOc(guard);
  const u = nuevoUuid();
  const { res } = await cargarCfdi(page, cfdiXml({ uuid: u, rfc: RFC_AJENO, fecha: today(), folio: numFolio() }), 'factura-otro-rfc.xml');
  expect(res.status()).toBeLessThan(300);
  const cfdi = await res.json();
  const cap = await abrirCaptura(page, u);
  guard.allowStatus(422);
  guard.expectApiMutationError(422);
  const post = page.waitForResponse((r) => /\/cuentas-por-pagar\/facturas$/.test(r.url()) && r.request().method() === 'POST');
  await cap.getByRole('button', { name: 'Capturar factura' }).click();
  const r = await post;
  expect(r.status()).toBe(422);
  await expect(toast(page)).toBeVisible();
  await page.waitForTimeout(500);
  await foto(page, 'V-CXP-02-rfc-emisor-distinto-al-del-proveedor-no-se-captura');
  const prob = JSON.parse(await r.text());
  expect(prob.code ?? JSON.stringify(prob)).toContain('CFDI_NO_UTILIZABLE');
  void cfdi;
});

test('V-CXP-03 factura con total fuera de tolerancia contra la OC no queda capturada y se informa la diferencia', async ({ page, guard }) => {
  await prepOc(guard);
  const oc = await crearOc(guard); // OC propia, sin facturar: la diferencia que se prueba es solo de importe
  const u = nuevoUuid();
  const subtotal = Math.round(SUBTOTAL * 1.3);
  await cargarCfdi(page, cfdiXml({ uuid: u, rfc: RFC_PROV, fecha: today(), folio: numFolio(), subtotal }), 'factura-total-mayor.xml');
  const cap = await abrirCaptura(page, u, oc.ocFolio);
  const post = page.waitForResponse((r) => /\/cuentas-por-pagar\/facturas$/.test(r.url()) && r.request().method() === 'POST');
  await cap.getByRole('button', { name: 'Capturar factura' }).click();
  const r = await post;
  const out = await r.json();
  await expect(page.getByText('Factura cancelada por diferencia en la conciliación')).toBeVisible();
  await expect(page.getByText('Rechazada por tolerancia').first()).toBeVisible();
  await page.waitForTimeout(800);
  await foto(page, 'V-CXP-03-total-fuera-de-tolerancia-factura-rechazada');
  expect(out.estado, 'Cancelada').toBe(5);
  expect(out.diferenciaContraOc).toBeGreaterThan(0);
  expect(out.motivoCancelacionTexto).toBeTruthy();
  const det = await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${out.id}`));
  expect(det.estado).toBe(out.estado);
});

test('V-CXP-04 factura con fecha de contabilización en un periodo contable cerrado no se captura', async ({ page, guard }) => {
  await prepOc(guard);
  const oc = await crearOc(guard);
  await recibirOc(guard, oc); // con lo recibido, el único motivo de rechazo que queda es el periodo cerrado
  const anio = new Date().getFullYear();
  sql(`update contabilidad.periodos_contables set estado=2 where anio=${anio} and numero=1`);
  const u = nuevoUuid();
  await cargarCfdi(page, cfdiXml({ uuid: u, rfc: RFC_PROV, fecha: today(), folio: numFolio() }), 'factura-contabilizada-en-enero.xml');
  const cap = await abrirCaptura(page, u, oc.ocFolio);
  await cap.getByText('Fecha contabilización').first().locator('xpath=following::input[1]').fill(`${anio}-01-15`);
  guard.allowStatus(422);
  guard.expectApiMutationError(422);
  const post = page.waitForResponse((r) => /\/cuentas-por-pagar\/facturas$/.test(r.url()) && r.request().method() === 'POST');
  await cap.getByRole('button', { name: 'Capturar factura' }).click();
  const r = await post;
  expect(r.status()).toBe(422);
  await expect(toast(page)).toBeVisible();
  await foto(page, 'V-CXP-04-periodo-contable-cerrado-no-admite-la-factura');
  const prob = JSON.parse(await r.text());
  expect(prob.code ?? JSON.stringify(prob)).toContain('CXP_PERIODO_CERRADO');
  expect(prob.detail).toContain('cerrado');
  // el servidor responde igual si se le llama directo
  const body = JSON.parse(r.request().postData() ?? '{}');
  const otra = await rechazo(await guard.api.post('/api/v1/cuentas-por-pagar/facturas', { ...body, uuidCfdi: nuevoUuid(), cfdiRecibidoId: null }, { 'Idempotency-Key': uuid() }), 422, 'CXP_PERIODO_CERRADO');
  expect(otra.detail).toContain('15/01');
  sql(`update contabilidad.periodos_contables set estado=1 where anio=${anio} and numero=1`);
});

test('V-CXP-05 factura sin recepción: se registra completa y el pasivo queda retenido por recepción pendiente (86e3yxpy4)', async ({ page, guard }) => {
  await prepOc(guard);
  const oc = await crearOc(guard); // OC autorizada, nada recibido todavía en Almacén
  const u = nuevoUuid(); const num = numFolio();
  await cargarCfdi(page, cfdiXml({ uuid: u, rfc: RFC_PROV, fecha: today(), folio: num }), 'factura-sin-recepcion.xml');
  const cap = await abrirCaptura(page, u, oc.ocFolio);
  const post = page.waitForResponse((r) => /\/cuentas-por-pagar\/facturas$/.test(r.url()) && r.request().method() === 'POST');
  await cap.getByRole('button', { name: 'Capturar factura' }).click();
  const r = await post;
  expect(r.status(), 'la factura mayor que lo recibido se registra completa (ya no se rechaza)').toBe(201);
  const fac = JSON.parse(await r.text());
  await page.waitForLoadState('networkidle');
  await foto(page, 'V-CXP-05-factura-sin-recepcion-retenida');
  expect(sql(`select count(*) from cuentas_por_pagar.facturas_proveedor where orden_compra_id = '${oc.ocId}'`), 'la factura quedó registrada').toBe('1');
  const det = await ok(await guard.api.get(`/api/v1/cuentas-por-pagar/facturas/${fac.id}`));
  expect(Number(det.retenido), 'el excedente sobre lo recibido (todo, aquí) queda retenido por recepción pendiente').toBeGreaterThan(0);
  void apiBaseURL; void captureBearer;
});
