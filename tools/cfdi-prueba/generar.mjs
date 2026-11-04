#!/usr/bin/env node
// Genera un CFDI de prueba para una OC leyendo el API del ERP (servidor de pruebas o local).
// Uso y requisitos: docs/operacion/cfdi-de-prueba.md
//
//   MILLET_TOKEN=<jwt> node tools/cfdi-prueba/generar.mjs --url <api> --oc <folio|id> [--cantidades 1=5,2=3] [--cargar]
//   Casos negativos: [--rfc-emisor RFC] [--tipo E] [--uuid UUID]
import { randomUUID } from 'node:crypto';
import { writeFile } from 'node:fs/promises';
import { parseArgs } from 'node:util';
import { armarReceptor, construirCfdi, receptorDeXml } from './cfdi.mjs';

process.on('uncaughtException', (e) => { console.error(`Error: ${e.message}`); process.exit(1); });

const { values: a } = parseArgs({
  options: {
    url: { type: 'string' },
    oc: { type: 'string' },
    cantidades: { type: 'string' },
    'rfc-receptor': { type: 'string' },
    'nombre-receptor': { type: 'string' },
    'cp-receptor': { type: 'string' },
    'rfc-emisor': { type: 'string' },
    tipo: { type: 'string', default: 'I' },
    uuid: { type: 'string' },
    cargar: { type: 'boolean', default: false },
    salida: { type: 'string', default: '.' },
  },
});
const token = process.env.MILLET_TOKEN?.replace(/^Bearer\s+/i, '');
if (!a.url || !a.oc || !token) {
  console.error('Uso: MILLET_TOKEN=<jwt> node tools/cfdi-prueba/generar.mjs --url <api> --oc <folio|id> [--cantidades 1=5,2=3] [--rfc-receptor RFC] [--nombre-receptor N] [--cp-receptor CP] [--rfc-emisor RFC] [--tipo I|E] [--uuid UUID] [--cargar] [--salida dir]');
  process.exit(2);
}
const base = a.url.replace(/\/+$/, '');
const auth = { Authorization: `Bearer ${token}` };

async function api(path, init = {}, como = 'json') {
  const res = await fetch(base + path, { ...init, headers: { ...auth, ...init.headers } });
  if (!res.ok) throw Object.assign(new Error(`${init.method ?? 'GET'} ${path} → ${res.status} ${await res.text()}`), { status: res.status });
  return res[como]();
}
const sinPermiso = (e) => { if (e.status !== 403) throw e; };

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

// Empresa receptora: Administración (admin.empresas.leer) o, para CxP, el receptor de los CFDI ya recibidos
// en CxP › CFDIs (cuentas_por_pagar.cfdis.leer), de preferencia uno real y no de prueba.
async function empresaReceptora() {
  const e = await api('/api/v1/admin/empresas?limit=200').then((r) => r.items?.find((x) => x.id === oc.empresaId), sinPermiso);
  if (e) return e;
  const items = await api('/api/v1/cuentas-por-pagar/cfdis?limit=20').then((r) => r.items ?? [], (err) => { sinPermiso(err); return []; });
  let dePrueba;
  for (const c of items) {
    const r = receptorDeXml(await api(`/api/v1/cuentas-por-pagar/cfdis/${c.id}/xml`, {}, 'text').catch(() => ''));
    if (r && c.serie !== 'PRUEBA') return r;
    dePrueba ??= r ?? undefined;
  }
  return dePrueba;
}
// Con RFC y nombre por línea de comandos no hace falta leer nada; con solo el RFC, la lectura es opcional.
const empresa = a['rfc-receptor'] && a['nombre-receptor'] ? null
  : a['rfc-receptor'] ? await empresaReceptora().catch(() => null) : await empresaReceptora();
const receptor = armarReceptor(empresa, { rfc: a['rfc-receptor'], nombre: a['nombre-receptor'], cp: a['cp-receptor'] });
const emisor = a['rfc-emisor'] ? { ...proveedor, rfc: a['rfc-emisor'].toUpperCase() } : proveedor;

if (oc.descuentoGlobalValor > 0)
  console.warn('Aviso: la OC tiene descuento global; este CFDI no lo reparte y CxP puede marcar diferencia.');

const cantidades = a.cantidades
  ? Object.fromEntries(a.cantidades.split(',').map((p) => p.split('=').map(Number)))
  : undefined;
const cfdi = construirCfdi({ oc, emisor, receptor, cantidades, tipo: a.tipo.toUpperCase(), ...(a.uuid && { uuid: a.uuid }) });
const archivo = `${a.salida.replace(/\/+$/, '')}/cfdi-prueba-${oc.folio}-${cfdi.uuid}.xml`;
await writeFile(archivo, cfdi.xml);
console.log(`OC ${oc.folio} · tipo ${a.tipo.toUpperCase()} · emisor ${emisor.rfc}${emisor.rfc !== proveedor.rfc ? ` (no es el proveedor ${proveedor.rfc})` : ''} · receptor ${receptor.rfc} ${receptor.razonSocial} CP ${receptor.codigoPostal ?? '00000'}`);
for (const l of cfdi.lineas) console.log(`  línea ${l.linea.posicion}: ${l.cantidad} × ${l.linea.precioUnitario} = ${l.importe.toFixed(2)} (IVA ${l.iva.toFixed(2)})`);
console.log(`Subtotal ${cfdi.subtotal.toFixed(2)} · IVA ${cfdi.iva.toFixed(2)} · Total ${cfdi.total.toFixed(2)} ${oc.moneda}`);
console.log(`UUID ${cfdi.uuid}\nXML: ${archivo}`);

if (a.cargar) {
  const form = new FormData();
  form.append('xml', new Blob([cfdi.xml], { type: 'text/xml' }), `cfdi-prueba-${oc.folio}.xml`);
  const r = await api('/api/v1/cuentas-por-pagar/cfdis/cargar', { method: 'POST', body: form, headers: { 'Idempotency-Key': randomUUID() } });
  console.log(`Cargado en CxP › CFDIs (id ${r.id}, estado ${r.estado}).`);
}
