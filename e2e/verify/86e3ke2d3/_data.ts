/**
 * P8.3 (86e3ke2d3) data. The CxP -> Tesoreria bridge and the payment events are not driven from any screen, so suppliers, invoices,
 * the authorized run, the bank payment and the pending payment notice are seeded by SQL straight into the TEST database (never dev).
 * Everything the task is about (portal login by emailed code, portal tables, CFDI P upload and linking, expediente upload,
 * Nueva corrida, notice mail) goes through the real UI / backend.
 */
import { execFileSync } from 'node:child_process';
import { envCfg, env } from '../../_harness/session';

export function sql(statement: string): string {
  if (env !== 'test') throw new Error('[P8.3] seeding is only allowed in the test environment');
  const url = new URL(process.env.TEST_DATABASE_URL ?? envCfg.db.url);
  if (!/_s\d+$|millet_test$/.test(url.pathname)) throw new Error(`[P8.3] refusing to seed ${url.pathname}`);
  return execFileSync('psql', [url.toString(), '-v', 'ON_ERROR_STOP=1', '-Atq', '-c', statement], {
    encoding: 'utf8',
    env: { ...process.env, PGPASSWORD: decodeURIComponent(url.password) },
  }).trim();
}
const esc = (s: string) => s.replace(/'/g, "''");
const uid = (n: number) => `00000019-0001-4000-8000-${String(n).padStart(12, '0')}`;
const day = (offset: number) => {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return d.toISOString().slice(0, 10);
};
function clabe(banco: string, plaza: string, cuenta: string): string {
  const base = banco + plaza + cuenta;
  const w = [3, 7, 1];
  const s = base.split('').reduce((a, d, i) => a + ((Number(d) * w[i % 3]) % 10), 0);
  return base + String((10 - (s % 10)) % 10);
}

export const MILLET_RFC = 'VMI0305129A4';
export const PROVS = {
  bajio: { id: uid(1), clave: 'PRV-2101', razon: 'Vidrio Flotado del Bajío S.A. de C.V.', rfc: 'VFB140822K59', email: 'cobranza@vidrioflotadobajio.example.com', cta: ['012', '180', '00412398765'], conOpinion: true },
  selladores: { id: uid(2), clave: 'PRV-2102', razon: 'Selladores y Adhesivos del Norte S.A. de C.V.', rfc: 'SAN110405Q72', email: 'pagos@selladoresnorte.example.com', cta: ['014', '580', '65504218873'], conOpinion: false },
  perfiles: { id: uid(3), clave: 'PRV-2103', razon: 'Perfiles de Aluminio Occidente S.A. de C.V.', rfc: 'PAO090317L38', email: 'facturacion@perfilesoccidente.example.com', cta: ['072', '320', '00871234560'], conOpinion: true },
  embalajes: { id: uid(4), clave: 'PRV-2104', razon: 'Embalajes y Tarimas de Puebla S.A. de C.V.', rfc: 'ETP120611H85', email: 'cobros@embalajespuebla.example.com', cta: ['021', '650', '40213398712'], conOpinion: true },
} as const;

// Fiscal UUIDs of the invoices (uppercase, as the SAT prints them).
export const UUID = {
  bajioPagada: '5C1D7A02-3B8E-4F61-9A47-0E6B2D8C1F10',
  bajioEnCorrida: '8E4B2C19-6D0A-4B73-A5F2-1C9D7E3B4A21',
  bajioSinProgramar: 'A3F90D55-12C7-4E88-B6D1-7F2A5C0E9B32',
  selladores1: '1B7E3D90-84AC-4D15-8F62-C3A09E5D7B43',
  selladores2: 'F2A64C18-95B3-4E07-A1D8-6B4E0C7F2A54',
  perfilesPpd: '3D8C5E71-0F2A-4B96-8C13-E5A7B9D04F65',
  embalajes: '9A0E6B34-71D5-4C28-B3F9-2D8C1E5A7B76',
  // complementos de pago (CFDI tipo P) del proveedor
  reppValido: 'C7B2E905-4A31-4D68-9E0F-5B3A8D1C6E87',
  reppAjeno: 'D4E81A63-2B90-4F57-A6C4-9E7D3B0F5A98',
};
export const IDS = {
  bajioPagada: uid(101), bajioEnCorrida: uid(102), bajioSinProgramar: uid(103),
  selladores1: uid(104), selladores2: uid(105), perfilesPpd: uid(106), embalajes: uid(107),
};
export const FACTURAS = {
  bajioPagada: { folio: 'F-47602', total: 23200 },
  bajioEnCorrida: { folio: 'F-48213', total: 99992 },
  bajioSinProgramar: { folio: 'F-48377', total: 17284 },
  selladores1: { folio: 'SN-5508', total: 11437.6 },
  selladores2: { folio: 'SN-5611', total: 8236.4 },
  perfilesPpd: { folio: 'A-10377', total: 48140 },
  embalajes: { folio: 'EP-7716', total: 14442 },
};
/** Payment date of the PPD invoice (also the FechaPago of the CFDI tipo P). */
export const FECHA_PAGO_PPD = day(-12);
export const VENCE_ENCORRIDA = day(5);

export function clean() {
  sql(`
    delete from cuentas_por_pagar.aviso_pago_portal where factura_proveedor_id::text like '00000019-%';
    delete from cuentas_por_pagar.codigo_portal where acceso_id in (select id from cuentas_por_pagar.acceso_portal_proveedor where proveedor_id::text like '00000019-%');
    delete from cuentas_por_pagar.acceso_portal_proveedor where proveedor_id::text like '00000019-%';
    delete from cuentas_por_pagar.pagos_proveedor_local where factura_proveedor_id::text like '00000019-%';
    delete from tesoreria.repp_pagos_proveedor where repp_id in (select id from tesoreria.repp_proveedor_recibido where factura_proveedor_id::text like '00000019-%');
    delete from tesoreria.repp_proveedor_recibido where factura_proveedor_id::text like '00000019-%';
    delete from tesoreria.aplicacion_pago_proveedor where proveedor_id::text like '00000019-%';
    delete from tesoreria.movimiento_bancario where id::text like '00000019-%';
    delete from tesoreria.corrida_pago_linea where corrida_id::text like '00000019-%';
    delete from tesoreria.corrida_pago where id::text like '00000019-%';
    delete from tesoreria.pasivo_pendiente_pago where proveedor_id::text like '00000019-%';
    delete from tesoreria.cuenta_bancaria where id::text like '00000019-%';
    delete from cuentas_por_pagar.facturas_proveedor where proveedor_id::text like '00000019-%';
    delete from compartido.adjuntos where entidad_id::text like '00000019-%';
    delete from compartido.proveedor_cuentas_bancarias where proveedor_id::text like '00000019-%';
    delete from compartido.proveedores where id::text like '00000019-%';`);
}

export function seed() {
  const emp = sql('select id from compartido.empresas limit 1');
  if (!emp) throw new Error('[P8.3] no empresa in the test DB');
  const suc = uid(900);
  const tipoOpinion = '00000011-0001-0000-0000-000000000007';
  clean();
  const cuenta = uid(800);
  sql(`insert into tesoreria.cuenta_bancaria (id, empresa_id, banco, numero_cuenta, clabe, moneda, activa, version, created_at, updated_at, titular, finalidad, sucursal)
    values ('${cuenta}', '${emp}', 'BBVA México', '0123456789', '${clabe('012', '180', '00123456789')}', 'MXN', true, 1, now(), now(), 'Vidrios Millet S.A. de C.V.', 'Pagos a proveedores', 'MID');`);

  Object.values(PROVS).forEach((p, i) => {
    sql(`insert into compartido.proveedores (id, clave, razon_social, rfc, tipo_persona, estatus, version, created_at, updated_at, condiciones_pago_dias, pais, origen, email)
      values ('${p.id}', '${p.clave}', '${esc(p.razon)}', '${p.rfc}', 1, 0, 1, now(), now(), 30, 'MEX', 0, '${p.email}');
    insert into compartido.proveedor_cuentas_bancarias (id, proveedor_id, banco, clabe, moneda, titular, predeterminada, estado, capturada_por_nombre, capturada_en, validada_por_nombre, validada_en, version, created_at, updated_at)
      values ('${uid(300 + i)}', '${p.id}', 'BBVA México', '${clabe(p.cta[0], p.cta[1], p.cta[2])}', 'MXN', '${esc(p.razon)}', true, 1, 'Compras', now(), 'Tesorería', now(), 1, now(), now());`);
    if (p.conOpinion)
      sql(`insert into compartido.adjuntos (id, tipo_entidad, entidad_id, tipo_documento_id, nombre_archivo, content_type, tamano_bytes, hash_sha256, blob_ref, vigente_hasta, subido_por_id, subido_en, version, created_at, updated_at)
        values ('${uid(400 + i)}', 'proveedor', '${p.id}', '${tipoOpinion}', 'opinion-32d.pdf', 'application/pdf', 20480, '${'ab'.repeat(32)}', 'opinion/${p.id}', '${day(20)}', '${uid(950)}', now(), 1, now(), now());`);
  });

  type F = { key: keyof typeof IDS; prov: keyof typeof PROVS; estado: number; vence: string; doc: string; metodo: 'PUE' | 'PPD'; pagado?: number; revision?: boolean; pasivo?: boolean };
  const defs: F[] = [
    { key: 'bajioPagada', prov: 'bajio', estado: 4, vence: day(-6), doc: day(-35), metodo: 'PUE', pagado: FACTURAS.bajioPagada.total },
    { key: 'bajioEnCorrida', prov: 'bajio', estado: 3, vence: VENCE_ENCORRIDA, doc: day(-18), metodo: 'PUE', pasivo: true },
    { key: 'bajioSinProgramar', prov: 'bajio', estado: 1, vence: day(25), doc: day(-3), metodo: 'PUE' },
    { key: 'selladores1', prov: 'selladores', estado: 3, vence: day(4), doc: day(-20), metodo: 'PUE', pasivo: true },
    { key: 'selladores2', prov: 'selladores', estado: 3, vence: day(9), doc: day(-14), metodo: 'PUE', pasivo: true },
    { key: 'perfilesPpd', prov: 'perfiles', estado: 2, vence: day(-10), doc: day(-40), metodo: 'PPD', pagado: FACTURAS.perfilesPpd.total, revision: true, pasivo: true },
    { key: 'embalajes', prov: 'embalajes', estado: 3, vence: day(6), doc: day(-12), metodo: 'PUE', pasivo: true },
  ];
  for (const f of defs) {
    const meta = FACTURAS[f.key];
    const sub = Math.round((meta.total / 1.16) * 100) / 100;
    const iva = Math.round((meta.total - sub) * 100) / 100;
    const uuid = UUID[f.key];
    const prov = PROVS[f.prov].id;
    sql(`insert into cuentas_por_pagar.facturas_proveedor (id, empresa_id, uuid_cfdi, proveedor_id, sucursal_id, folio_proveedor, fecha_documento, fecha_contabilizacion, fecha_vencimiento, moneda,
        subtotal, descuentos, impuestos_trasladados, retenciones, total, estado, diferencia_contra_oc, redondeo_aplicado, anticipo_aplicado_total, nc_aplicadas_total, cargos_aplicados_total, importe_pagado,
        en_revision, motivo_revision_id, fecha_entrada_revision, version, created_at, updated_at, repp_recibido, metodo_pago)
      values ('${IDS[f.key]}', '${emp}', '${uuid}', '${prov}', '${suc}', '${meta.folio}', '${f.doc} 12:00:00+00', '${f.doc} 12:00:00+00', '${f.vence}', 'MXN',
        ${sub}, 0, ${iva}, 0, ${meta.total}, ${f.estado}, 0, 0, 0, 0, 0, ${f.pagado ?? 0},
        ${f.revision ? 'true' : 'false'}, ${f.revision ? "'00000007-1001-0000-0000-000000000009'" : 'null'}, ${f.revision ? `'${day(-5)} 12:00:00+00'` : 'null'}, 1, now(), now(), false, '${f.metodo}');`);
    if (f.pasivo)
      sql(`insert into tesoreria.pasivo_pendiente_pago (id, empresa_id, factura_proveedor_id, proveedor_id, monto_total, saldo_pendiente, moneda, fecha_vencimiento, uuid_cfdi, folio_proveedor, metodo_pago,
          recibido_en, version, created_at, updated_at, beneficiario_id, origen_id, origen_tipo, tipo_beneficiario, pago_bloqueado, subtotal, impuestos_trasladados, retenciones)
        values ('${uid(500 + defs.indexOf(f))}', '${emp}', '${IDS[f.key]}', '${prov}', ${meta.total}, ${f.pagado ? 0 : meta.total}, 'MXN', '${f.vence}', '${uuid}', '${meta.folio}', '${f.metodo}',
          '${f.doc} 15:00:00+00', 1, now(), now(), '${prov}', '${IDS[f.key]}', 'Factura', 'Proveedor', false, ${sub}, ${iva}, 0);`);
  }

  // Corrida autorizada con la factura F-48213 de Vidrio Flotado del Bajío.
  const corrida = uid(600);
  sql(`insert into tesoreria.corrida_pago (id, empresa_id, cuenta_bancaria_id, estado, total, moneda, solicitada_por, autorizada_por, creada_en, version, created_at, updated_at, autorizada_en, firmas_recibidas, firmas_requeridas, nombre)
      values ('${corrida}', '${emp}', '${cuenta}', 3, ${FACTURAS.bajioEnCorrida.total}, 'MXN', '${uid(951)}', '${uid(952)}', now() - interval '1 day', 1, now(), now(), now(), 1, 1, 'Corrida semana 44');
    insert into tesoreria.corrida_pago_linea (id, corrida_id, factura_proveedor_id, importe_programado, ejecutada, version, created_at, updated_at, bruto, proveedor_id, retenciones, viva)
      values ('${uid(601)}', '${corrida}', '${IDS.bajioEnCorrida}', ${FACTURAS.bajioEnCorrida.total}, false, 1, now(), now(), ${FACTURAS.bajioEnCorrida.total}, '${PROVS.bajio.id}', 0, true);`);

  // Pagos ya hechos por banco: Bajío (PUE, con aviso pendiente) y Perfiles (PPD, sin complemento).
  const pago = (n: number, factura: keyof typeof IDS, prov: keyof typeof PROVS, fecha: string, ref: string) => {
    const monto = FACTURAS[factura].total;
    sql(`insert into tesoreria.movimiento_bancario (id, empresa_id, cuenta_bancaria_id, sentido, monto, moneda, fecha_valor, referencia_bancaria, concepto_id, estado_aplicacion, estado_conciliacion, creado_por, creado_en, version, created_at, updated_at)
        values ('${uid(700 + n)}', '${emp}', '${cuenta}', 2, ${monto}, 'MXN', '${fecha}', '${ref}', '0000000b-1001-0000-0000-000000000001', 3, 1, '${uid(951)}', now(), 1, now(), now());
      insert into tesoreria.aplicacion_pago_proveedor (id, movimiento_id, factura_proveedor_id, proveedor_id, importe_aplicado, revertida, creado_en, version, created_at, updated_at)
        values ('${uid(710 + n)}', '${uid(700 + n)}', '${IDS[factura]}', '${PROVS[prov].id}', ${monto}, false, now(), 1, now(), now());
      insert into cuentas_por_pagar.pagos_proveedor_local (id, empresa_id, factura_proveedor_id, pago_id, fecha_pago, importe, cubierto_repp, revertido, version, created_at, updated_at)
        values ('${uid(720 + n)}', '${emp}', '${IDS[factura]}', '${uid(710 + n)}', '${fecha}', ${monto}, 0, false, 1, now(), now());
      insert into cuentas_por_pagar.aviso_pago_portal (id, empresa_id, factura_proveedor_id, pago_id, monto, fecha_pago, destinatarios, version, created_at, updated_at)
        values ('${uid(730 + n)}', '${emp}', '${IDS[factura]}', '${uid(710 + n)}', ${monto}, '${fecha}', 0, 1, now(), now());`);
  };
  pago(1, 'bajioPagada', 'bajio', day(-5), 'SPEI2610262201');
  pago(2, 'perfilesPpd', 'perfiles', FECHA_PAGO_PPD, 'SPEI2610192317');

  // Accesos al portal de Selladores y Perfiles por SQL; el de Vidrio Flotado se da de alta desde la ficha (pantalla).
  for (const [i, p] of [PROVS.selladores, PROVS.perfiles].entries())
    sql(`insert into cuentas_por_pagar.acceso_portal_proveedor (id, empresa_id, proveedor_id, email, activo, version, created_at, updated_at)
      values ('${uid(850 + i)}', '${emp}', '${p.id}', '${p.email}', true, 1, now(), now());`);
}

/** Avisos listos para que el worker los mande: los inserta `pago()`; esta lectura dice cuántos faltan. */
export const avisosPendientes = () => Number(sql(`select count(*) from cuentas_por_pagar.aviso_pago_portal where enviado_en is null and factura_proveedor_id::text like '00000019-%'`));

// ---------- Mailpit ----------
const MAILPIT = process.env.MAILPIT_URL ?? 'http://127.0.0.1:8025';
type Msg = { ID: string; Subject: string; To: Array<{ Address: string }>; Created: string };
export const mailpitVaciar = () => fetch(`${MAILPIT}/api/v1/messages`, { method: 'DELETE' });
export async function mailpitMensajes(para: string, asunto: RegExp): Promise<Msg[]> {
  const r = await fetch(`${MAILPIT}/api/v1/messages?limit=100`);
  const j = (await r.json()) as { messages: Msg[] };
  return j.messages.filter((m) => m.To.some((t) => t.Address.toLowerCase() === para.toLowerCase()) && asunto.test(m.Subject));
}
export async function esperarMensaje(para: string, asunto: RegExp, timeoutMs = 20_000): Promise<Msg> {
  const fin = Date.now() + timeoutMs;
  for (;;) {
    const ms = await mailpitMensajes(para, asunto);
    if (ms.length) return ms[0];
    if (Date.now() > fin) throw new Error(`Mailpit: no llegó "${asunto}" para ${para}`);
    await new Promise((r) => setTimeout(r, 700));
  }
}
export async function codigoDe(para: string): Promise<string> {
  const m = await esperarMensaje(para, /código de acceso/i);
  const d = (await (await fetch(`${MAILPIT}/api/v1/message/${m.ID}`)).json()) as { Text?: string; HTML?: string };
  const code = /\b(\d{6})\b/.exec(`${d.Text ?? ''} ${d.HTML ?? ''}`)?.[1];
  if (!code) throw new Error('Mailpit: el correo no trae un código de 6 dígitos');
  return code;
}
export const mailpitUrl = (id: string) => `${MAILPIT}/view/${id}`;

// ---------- CFDI tipo P (sin timbrar: el sistema no valida sello, solo estructura, RFC y UUID) ----------
export function xmlComplementoPago(o: { uuid: string; rfcEmisor: string; emisor: string; docRelacionado: string; fechaPago: string; monto: number; folio: string }): string {
  const m = o.monto.toFixed(2);
  const hoy = day(-1);
  return `<?xml version="1.0" encoding="UTF-8"?>
<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:pago20="http://www.sat.gob.mx/Pagos20" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Serie="CP" Folio="${o.folio}" Fecha="${hoy}T10:15:00" SubTotal="0" Moneda="XXX" Total="0" TipoDeComprobante="P" Exportacion="01" LugarExpedicionCP="44100">
  <cfdi:Emisor Rfc="${o.rfcEmisor}" Nombre="${esc(o.emisor)}" RegimenFiscal="601"/>
  <cfdi:Receptor Rfc="${MILLET_RFC}" Nombre="VIDRIOS MILLET" DomicilioFiscalReceptor="64000" RegimenFiscalReceptor="601" UsoCFDI="CP01"/>
  <cfdi:Conceptos>
    <cfdi:Concepto ClaveProdServ="84111506" Cantidad="1" ClaveUnidad="ACT" Descripcion="Pago" ValorUnitario="0" Importe="0" ObjetoImp="01"/>
  </cfdi:Conceptos>
  <cfdi:Complemento>
    <pago20:Pagos Version="2.0">
      <pago20:Totales MontoTotalPagos="${m}"/>
      <pago20:Pago FechaPago="${o.fechaPago}T12:00:00" FormaDePagoP="03" MonedaP="MXN" Monto="${m}" TipoCambioP="1">
        <pago20:DoctoRelacionado IdDocumento="${o.docRelacionado}" MonedaDR="MXN" EquivalenciaDR="1" NumParcialidad="1" ImpSaldoAnt="${m}" ImpPagado="${m}" ImpSaldoInsoluto="0.00" ObjetoImpDR="01"/>
      </pago20:Pago>
    </pago20:Pagos>
    <tfd:TimbreFiscalDigital Version="1.1" UUID="${o.uuid}" FechaTimbrado="${hoy}T10:16:00" RfcProvCertif="SPR190613I52" SelloCFD="x" NoCertificadoSAT="00001000000500000000" SelloSAT="x"/>
  </cfdi:Complemento>
</cfdi:Comprobante>`;
}

/** PDF mínimo válido para el expediente (opinión de cumplimiento 32-D). */
export const PDF_OPINION = Buffer.from(
  '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n' +
    '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 144]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n' +
    '4 0 obj<</Length 60>>stream\nBT /F1 14 Tf 20 80 Td (Opinion de cumplimiento 32-D positiva) Tj ET\nendstream endobj\n' +
    '5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\ntrailer<</Root 1 0 R/Size 6>>\n%%EOF\n',
  'latin1',
);
