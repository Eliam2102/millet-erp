// Arma el XML de un CFDI 4.0 de ingreso, sintético y sin timbrar, a partir de una OC
// (respuesta de GET /api/v1/compras/ordenes/{id}). Solo para el ambiente de pruebas:
// no lleva sello ni certificado reales y no se manda a ningún PAC ni al SAT.
import { randomUUID } from 'node:crypto';

// Redondeo decimal a centavos (37.035 → 37.04), sin el error binario de Math.round(n * 100).
const r2 = (n) => Number(Math.round(Number(`${n.toFixed(8)}e2`)) + 'e-2');
const m2 = (n) => r2(n).toFixed(2);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' })[c]);
const tasaTxt = (t) => t.toFixed(6);

/** Fecha local de México en formato CFDI (yyyy-MM-ddTHH:mm:ss). */
export function fechaCfdi(d = new Date()) {
  return d.toLocaleString('sv-SE', { timeZone: 'America/Mexico_City' }).replace(' ', 'T');
}

/**
 * Cantidad a facturar por línea cuando no se indica: lo recibido y aún no facturado
 * (servicios: la cantidad completa si la OC ya tiene el servicio confirmado).
 * Es lo mismo que CxP deja capturar (FACTURA_EXCEDE_RECIBIDO).
 */
export function cantidadPorOmision(linea, servicioConfirmado) {
  const base = linea.esServicio ? (servicioConfirmado ? linea.cantidad : 0) : linea.cantidadRecibida ?? 0;
  return Math.max(0, base - (linea.cantidadFacturada ?? 0));
}

/**
 * @param {object} p
 * @param {object} p.oc OC tal como la devuelve el API (camelCase).
 * @param {{rfc: string, razonSocial: string}} p.emisor Proveedor de la OC.
 * @param {{rfc: string, razonSocial: string, codigoPostal?: string}} p.receptor Empresa (Millet).
 * @param {Record<number, number>} [p.cantidades] Posición de línea → cantidad (parcial). Sin ella, cantidadPorOmision.
 * @param {string} [p.uuid] UUID del timbre; por omisión uno aleatorio.
 * @param {string} [p.fecha] Fecha CFDI; por omisión ahora (hora de México).
 */
export function construirCfdi({ oc, emisor, receptor, cantidades, uuid = randomUUID().toUpperCase(), fecha = fechaCfdi() }) {
  if (!emisor?.rfc) throw new Error('El proveedor de la OC no tiene RFC.');
  if (!receptor?.rfc) throw new Error('Falta el RFC de la empresa receptora.');
  const servicioConfirmado = Boolean(oc.servicioConfirmadoEn);
  const lineas = [];
  for (const l of oc.lineas ?? []) {
    const q = cantidades ? cantidades[l.posicion] ?? 0 : cantidadPorOmision(l, servicioConfirmado);
    if (!(q > 0)) continue;
    const pendiente = l.cantidad - (l.cantidadFacturada ?? 0);
    if (q > pendiente + 1e-9) throw new Error(`Línea ${l.posicion}: ${q} excede lo pendiente de facturar (${pendiente}).`);
    const importe = r2(q * l.precioUnitario);
    // SubtotalLinea ya trae el descuento de la línea; la base proporcional es lo que CxP espera.
    const base = l.cantidad ? Math.min(importe, r2(q * l.subtotalLinea / l.cantidad)) : importe;
    const descuento = r2(importe - base);
    const tasa = l.subtotalLinea > 0 ? Math.round((l.ivaImporte / l.subtotalLinea) * 100) / 100 : 0;
    const iva = r2(base * tasa);
    const retencion = l.retencionIsr && l.cantidad ? r2(l.retencionIsr * q / l.cantidad) : 0;
    lineas.push({ linea: l, cantidad: q, importe, descuento: descuento > 0 ? descuento : 0, base, tasa, iva, retencion });
  }
  if (lineas.length === 0) throw new Error('No hay nada que facturar: ninguna línea tiene cantidad recibida sin facturar. Indica --cantidades.');

  const subtotal = r2(lineas.reduce((s, l) => s + l.importe, 0));
  const descuento = r2(lineas.reduce((s, l) => s + l.descuento, 0));
  const iva = r2(lineas.reduce((s, l) => s + l.iva, 0));
  const retenciones = r2(lineas.reduce((s, l) => s + l.retencion, 0));
  const total = r2(subtotal - descuento + iva - retenciones);
  const porTasa = new Map();
  for (const l of lineas) {
    const t = porTasa.get(l.tasa) ?? { base: 0, iva: 0 };
    porTasa.set(l.tasa, { base: t.base + l.base, iva: t.iva + l.iva });
  }

  const moneda = oc.moneda || 'MXN';
  const metodoPago = oc.metodoPago || 'PPD';
  const conceptos = lineas.map((l) => {
    const art = l.linea;
    const ret = l.retencion > 0
      ? `<cfdi:Retenciones><cfdi:Retencion Base="${m2(l.base)}" Impuesto="001" TipoFactor="Tasa" TasaOCuota="${tasaTxt(l.retencion / l.base)}" Importe="${m2(l.retencion)}"/></cfdi:Retenciones>`
      : '';
    return `    <cfdi:Concepto ClaveProdServ="01010101"${art.articuloClave ? ` NoIdentificacion="${esc(art.articuloClave)}"` : ''} Cantidad="${l.cantidad}" ClaveUnidad="H87" Unidad="${esc(art.unidadMedida)}" Descripcion="${esc(art.articuloNombre || art.descripcionExtendida || `Línea ${art.posicion}`)}" ValorUnitario="${art.precioUnitario}" Importe="${m2(l.importe)}"${l.descuento ? ` Descuento="${m2(l.descuento)}"` : ''} ObjetoImp="02">
      <cfdi:Impuestos><cfdi:Traslados><cfdi:Traslado Base="${m2(l.base)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="${tasaTxt(l.tasa)}" Importe="${m2(l.iva)}"/></cfdi:Traslados>${ret}</cfdi:Impuestos>
    </cfdi:Concepto>`;
  }).join('\n');
  const traslados = [...porTasa].map(([t, v]) =>
    `<cfdi:Traslado Base="${m2(v.base)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="${tasaTxt(t)}" Importe="${m2(v.iva)}"/>`).join('');
  const folio = uuid.slice(0, 8);

  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<!-- CFDI DE PRUEBA generado por tools/cfdi-prueba para la OC ${esc(oc.folio)}. Sin timbrar: no tiene validez fiscal. -->
<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Serie="PRUEBA" Folio="${folio}" Fecha="${fecha}" Sello="SIN-SELLO-PRUEBA" FormaPago="${metodoPago === 'PUE' ? '03' : '99'}" NoCertificado="00000000000000000000" Certificado="" SubTotal="${m2(subtotal)}"${descuento ? ` Descuento="${m2(descuento)}"` : ''} Moneda="${moneda}"${moneda !== 'MXN' ? ` TipoCambio="${oc.tipoCambio ?? 1}"` : ''} Total="${m2(total)}" TipoDeComprobante="I" Exportacion="01" MetodoPago="${metodoPago}" LugarExpedicion="${receptor.codigoPostal || '00000'}">
  <cfdi:Emisor Rfc="${esc(emisor.rfc)}" Nombre="${esc(emisor.razonSocial)}" RegimenFiscal="601"/>
  <cfdi:Receptor Rfc="${esc(receptor.rfc)}" Nombre="${esc(receptor.razonSocial)}" DomicilioFiscalReceptor="${receptor.codigoPostal || '00000'}" RegimenFiscalReceptor="601" UsoCFDI="${oc.usoCfdiClave || 'G03'}"/>
  <cfdi:Conceptos>
${conceptos}
  </cfdi:Conceptos>
  <cfdi:Impuestos${retenciones ? ` TotalImpuestosRetenidos="${m2(retenciones)}"` : ''} TotalImpuestosTrasladados="${m2(iva)}">${retenciones ? `<cfdi:Retenciones><cfdi:Retencion Impuesto="001" Importe="${m2(retenciones)}"/></cfdi:Retenciones>` : ''}<cfdi:Traslados>${traslados}</cfdi:Traslados></cfdi:Impuestos>
  <cfdi:Complemento>
    <tfd:TimbreFiscalDigital Version="1.1" UUID="${uuid}" FechaTimbrado="${fecha}" RfcProvCertif="SPR190613I52" SelloCFD="SIN-SELLO-PRUEBA" NoCertificadoSAT="00000000000000000000" SelloSAT="SIN-SELLO-PRUEBA"/>
  </cfdi:Complemento>
</cfdi:Comprobante>
`;
  return { xml, uuid, subtotal, descuento, iva, retenciones, total, lineas };
}
