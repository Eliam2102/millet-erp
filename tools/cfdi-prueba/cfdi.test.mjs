// node --test tools/cfdi-prueba/cfdi.test.mjs
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { armarReceptor, construirCfdi, receptorDeXml } from './cfdi.mjs';

const linea = (o) => ({
  posicion: 1, articuloClave: 'ART-1', articuloNombre: 'Resma "carta" & bond', unidadMedida: 'PZA',
  cantidad: 10, precioUnitario: 95, subtotalLinea: 950, ivaImporte: 152, retencionIsr: null,
  cantidadRecibida: 10, cantidadFacturada: 0, esServicio: false, ...o,
});
const oc = (lineas, o = {}) => ({ folio: 'OC-MID2026-000009', moneda: 'MXN', metodoPago: 'PPD', lineas, ...o });
const emisor = { rfc: 'AAA010101AAA', razonSocial: 'Proveedor Demo' };
const receptor = { rfc: 'MIL010101AAA', razonSocial: 'Millet SA', codigoPostal: '97000' };
const attr = (xml, tag, name) => xml.match(new RegExp(`<${tag}\\b[^>]*\\s${name}="([^"]*)"`))?.[1];

test('emisor = proveedor de la OC y receptor = empresa', () => {
  const { xml } = construirCfdi({ oc: oc([linea()]), emisor, receptor });
  assert.equal(attr(xml, 'cfdi:Emisor', 'Rfc'), 'AAA010101AAA');
  assert.equal(attr(xml, 'cfdi:Receptor', 'Rfc'), 'MIL010101AAA');
  assert.equal(attr(xml, 'cfdi:Comprobante', 'Version'), '4.0');
  assert.equal(attr(xml, 'cfdi:Comprobante', 'TipoDeComprobante'), 'I');
});

test('totales e IVA por línea cuadran con la OC', () => {
  const r = construirCfdi({ oc: oc([linea(), linea({ posicion: 2, cantidad: 3, cantidadRecibida: 3, precioUnitario: 12.345, subtotalLinea: 37.04, ivaImporte: 5.93 })]), emisor, receptor });
  assert.equal(r.subtotal, 987.04);
  assert.equal(r.iva, 157.93);
  assert.equal(r.total, 1144.97);
  assert.equal(attr(r.xml, 'cfdi:Comprobante', 'SubTotal'), '987.04');
  assert.equal(attr(r.xml, 'cfdi:Comprobante', 'Total'), '1144.97');
  assert.equal(attr(r.xml, 'cfdi:Impuestos', 'TotalImpuestosTrasladados'), '157.93');
  assert.equal(attr(r.xml, 'cfdi:Traslado', 'TasaOCuota'), '0.160000');
});

test('cantidades parciales prorratean importe e IVA', () => {
  const r = construirCfdi({ oc: oc([linea({ cantidadRecibida: 4 })]), emisor, receptor, cantidades: { 1: 4 } });
  assert.equal(r.subtotal, 380);
  assert.equal(r.iva, 60.8);
  assert.equal(r.total, 440.8);
  assert.equal(attr(r.xml, 'cfdi:Concepto', 'Cantidad'), '4');
});

test('por omisión factura lo recibido sin facturar y omite líneas sin pendiente', () => {
  const r = construirCfdi({ oc: oc([linea({ cantidadRecibida: 6, cantidadFacturada: 2 }), linea({ posicion: 2, cantidadRecibida: 0 })]), emisor, receptor });
  assert.equal(r.lineas.length, 1);
  assert.equal(r.lineas[0].cantidad, 4);
});

test('descuento de línea: la base es el subtotal neto de la OC', () => {
  const r = construirCfdi({ oc: oc([linea({ subtotalLinea: 900, ivaImporte: 144 })]), emisor, receptor });
  assert.equal(r.descuento, 50);
  assert.equal(r.iva, 144);
  assert.equal(r.total, 1044);
  assert.equal(attr(r.xml, 'cfdi:Comprobante', 'Descuento'), '50.00');
});

test('IVA 0% y moneda extranjera con tipo de cambio', () => {
  const r = construirCfdi({ oc: oc([linea({ ivaImporte: 0 })], { moneda: 'USD', tipoCambio: 17.5 }), emisor, receptor });
  assert.equal(r.iva, 0);
  assert.equal(attr(r.xml, 'cfdi:Comprobante', 'TipoCambio'), '17.5');
});

test('UUID aleatorio distinto en cada CFDI y en el timbre', () => {
  const uuids = new Set(Array.from({ length: 50 }, () => construirCfdi({ oc: oc([linea()]), emisor, receptor }).uuid));
  assert.equal(uuids.size, 50);
  const r = construirCfdi({ oc: oc([linea()]), emisor, receptor });
  assert.match(r.uuid, /^[0-9A-F]{8}-[0-9A-F]{4}-4[0-9A-F]{3}-[89AB][0-9A-F]{3}-[0-9A-F]{12}$/);
  assert.equal(attr(r.xml, 'tfd:TimbreFiscalDigital', 'UUID'), r.uuid);
});

test('escapa texto y rechaza cantidades de más o una OC sin pendiente', () => {
  const { xml } = construirCfdi({ oc: oc([linea()]), emisor, receptor });
  assert.ok(xml.includes('Resma &quot;carta&quot; &amp; bond'));
  assert.throws(() => construirCfdi({ oc: oc([linea()]), emisor, receptor, cantidades: { 1: 11 } }), /excede/);
  assert.throws(() => construirCfdi({ oc: oc([linea({ cantidadRecibida: 0 })]), emisor, receptor }), /No hay nada que facturar/);
  assert.throws(() => construirCfdi({ oc: oc([linea()]), emisor: { rfc: '' }, receptor }), /RFC/);
});

test('casos negativos: tipo Egreso, UUID repetido y emisor distinto', () => {
  const uuid = '15a75665-1111-4222-8333-444455556666';
  const r = construirCfdi({ oc: oc([linea()]), emisor: { ...emisor, rfc: 'XAXX010101000' }, receptor, tipo: 'E', uuid });
  assert.equal(attr(r.xml, 'cfdi:Comprobante', 'TipoDeComprobante'), 'E');
  assert.equal(attr(r.xml, 'cfdi:Emisor', 'Rfc'), 'XAXX010101000');
  assert.equal(r.uuid, uuid.toUpperCase());
  assert.equal(attr(r.xml, 'tfd:TimbreFiscalDigital', 'UUID'), uuid.toUpperCase());
  assert.throws(() => construirCfdi({ oc: oc([linea()]), emisor, receptor, tipo: 'P' }), /usa I \(ingreso\) o E/);
  assert.throws(() => construirCfdi({ oc: oc([linea()]), emisor, receptor, uuid: 'no-es-uuid' }), /UUID inválido/);
});

test('receptor: lo leído del XML de un CFDI recibido y lo de la línea de comandos manda', () => {
  const { xml } = construirCfdi({ oc: oc([linea()]), emisor, receptor: { rfc: 'MIL010101AAA', razonSocial: 'Millet & Cía', codigoPostal: '97000' } });
  const leido = receptorDeXml(xml);
  assert.deepEqual(leido, { rfc: 'MIL010101AAA', razonSocial: 'Millet & Cía', codigoPostal: '97000' });
  assert.equal(receptorDeXml('<cfdi:Comprobante/>'), null);

  assert.deepEqual(armarReceptor(leido), leido);
  assert.deepEqual(armarReceptor(leido, { cp: '97100', nombre: 'Otra' }), { rfc: 'MIL010101AAA', razonSocial: 'Otra', codigoPostal: '97100' });
  // Otro RFC: no hereda nombre ni CP de la empresa.
  assert.deepEqual(armarReceptor(leido, { rfc: 'otr010101aaa' }), { rfc: 'OTR010101AAA', razonSocial: 'Millet', codigoPostal: undefined });
  // El CP 00000 de un CFDI de prueba no se reutiliza.
  assert.equal(armarReceptor({ ...leido, codigoPostal: '00000' }).codigoPostal, undefined);
  assert.throws(() => armarReceptor(null), /--rfc-receptor/);
  assert.throws(() => armarReceptor(leido, { cp: '123' }), /5 dígitos/);

  const r = construirCfdi({ oc: oc([linea()]), emisor, receptor: armarReceptor(leido, { cp: '97100', nombre: 'Otra' }) });
  assert.equal(attr(r.xml, 'cfdi:Receptor', 'Nombre'), 'Otra');
  assert.equal(attr(r.xml, 'cfdi:Receptor', 'DomicilioFiscalReceptor'), '97100');
  assert.equal(attr(r.xml, 'cfdi:Comprobante', 'LugarExpedicion'), '97100');
});
