#!/usr/bin/env node
// Genera un CFDI de prueba para una OC leyendo el API del ERP (servidor de pruebas o local).
// Uso y requisitos: docs/operacion/cfdi-de-prueba.md
//
//   MILLET_TOKEN=<jwt> node tools/cfdi-prueba/generar.mjs --url <api> --oc <folio|id> [--cantidades 1=5,2=3] [--cargar]
import { randomUUID } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';
import { construirCfdi } from './cfdi.mjs';

process.on('uncaughtException', (e) => { console.error(`Error: ${e.message}`); process.exit(1); });

const { values: a } = parseArgs({
  options: {
    url: { type: 'string' },
    oc: { type: 'string' },
    cantidades: { type: 'string' },
    'rfc-receptor': { type: 'string' },
    cargar: { type: 'boolean', default: false },
    salida: { type: 'string', default: '.' },
  },
});
const token = process.env.MILLET_TOKEN?.replace(/^Bearer\s+/i, '');
if (!a.url || !a.oc || !token) {
  console.error('Uso: MILLET_TOKEN=<jwt> node tools/cfdi-prueba/generar.mjs --url <api> --oc <folio|id> [--cantidades 1=5,2=3] [--rfc-receptor RFC] [--cargar] [--salida dir]');
  process.exit(2);
}
const base = a.url.replace(/\/+$/, '');
const auth = { Authorization: `Bearer ${token}` };

async function api(path, init = {}) {
  const res = await fetch(base + path, { ...init, headers: { ...auth, ...init.headers } });
  if (!res.ok) throw Object.assign(new Error(`${init.method ?? 'GET'} ${path} → ${res.status} ${await res.text()}`), { status: res.status });
  return res.json();
}

// Nunca en producción: el API dice su ambiente en /api/version (anónimo).
const { ambiente } = await fetch(`${base}/api/version`).then((r) => r.json());
if (!ambiente || /^prod/i.test(ambiente)) {
  console.error(`Ambiente «${ambiente}»: el CFDI de prueba no se genera en producción.`);
  process.exit(1);
}

const esId = /^[0-9a-f-]{36}$/i.test(a.oc);
const ocId = esId ? a.oc
  : (await api(`/api/v1/compras/ordenes?q=${encodeURIComponent(a.oc)}&pageSize=20`)).items
    ?.find((o) => o.folio?.toUpperCase() === a.oc.toUpperCase())?.id;
if (!ocId) throw new Error(`No encontré la OC ${a.oc}.`);
const oc = await api(`/api/v1/compras/ordenes/${ocId}`);
const proveedor = await api(`/api/v1/catalogos/proveedores/${oc.proveedorId}`);

let receptor;
if (a['rfc-receptor']) {
  receptor = { rfc: a['rfc-receptor'].toUpperCase(), razonSocial: 'Millet' };
} else {
  try {
    receptor = (await api('/api/v1/admin/empresas?limit=200')).items?.find((e) => e.id === oc.empresaId);
  } catch (e) {
    if (e.status !== 403) throw e;
  }
  if (!receptor) throw new Error('No pude leer el RFC de la empresa (permiso admin.empresas.leer). Pásalo con --rfc-receptor.');
}

if (oc.descuentoGlobalValor > 0)
  console.warn('Aviso: la OC tiene descuento global; este CFDI no lo reparte y CxP puede marcar diferencia.');

const cantidades = a.cantidades
  ? Object.fromEntries(a.cantidades.split(',').map((p) => p.split('=').map(Number)))
  : undefined;
const cfdi = construirCfdi({ oc, emisor: proveedor, receptor, cantidades });
const archivo = `${a.salida.replace(/\/+$/, '')}/cfdi-prueba-${oc.folio}-${cfdi.uuid}.xml`;
await writeFile(archivo, cfdi.xml);
console.log(`OC ${oc.folio} · emisor ${proveedor.rfc} · receptor ${receptor.rfc}`);
for (const l of cfdi.lineas) console.log(`  línea ${l.linea.posicion}: ${l.cantidad} × ${l.linea.precioUnitario} = ${l.importe.toFixed(2)} (IVA ${l.iva.toFixed(2)})`);
console.log(`Subtotal ${cfdi.subtotal.toFixed(2)} · IVA ${cfdi.iva.toFixed(2)} · Total ${cfdi.total.toFixed(2)} ${oc.moneda}`);
console.log(`UUID ${cfdi.uuid}\nXML: ${archivo}`);

if (a.cargar) {
  const form = new FormData();
  form.append('xml', new Blob([cfdi.xml], { type: 'text/xml' }), `cfdi-prueba-${oc.folio}.xml`);
  const r = await api('/api/v1/cuentas-por-pagar/cfdis/cargar', { method: 'POST', body: form, headers: { 'Idempotency-Key': randomUUID() } });
  console.log(`Cargado en CxP › CFDIs (id ${r.id}, estado ${r.estado}).`);
}
