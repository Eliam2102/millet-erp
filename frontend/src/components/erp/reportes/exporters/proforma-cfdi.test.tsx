// @vitest-environment node
import { inflateSync } from 'node:zlib';
import { describe, it, expect } from 'vitest';
import { pdf } from '@react-pdf/renderer';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { ProformaCfdiDocument } from './ProformaCfdiDocument';
import { nombreArchivoProforma } from './proforma-cfdi-exporter';

/** Extrae operadores de texto del PDF real (mismo patrón que borrador-cfdi.test). */
function textoPdf(bytes: Buffer): string {
  const source = bytes.toString('latin1');
  return [...source.matchAll(/stream\r?\n([\s\S]*?)\r?\nendstream/g)]
    .flatMap((match) => {
      let stream: string;
      try {
        stream = inflateSync(Buffer.from(match[1], 'latin1')).toString('latin1');
      } catch {
        stream = match[1];
      }
      return [...stream.matchAll(/\[([^\]]*)\]\s*TJ/g)].map((operator) =>
        [...operator[1].matchAll(/<([0-9a-f]+)>/gi)]
          .map((hex) => Buffer.from(hex[1], 'hex').toString('latin1'))
          .join(''),
      );
    })
    .join('\n');
}

const PROFORMA: ProformaPedidoResponse = {
  id: 'pf-1',
  pedidoFacturableId: 'p-1',
  folio: 'PF-MID-001-2',
  version: 2,
  generadaEn: '2026-10-21T15:30:00Z',
  generadaPor: 'Ana López',
  numeroPedido: 'MID-001',
  avisos: ['Receptor sin régimen fiscal'],
  contenido: {
    emisor: {
      rfc: 'MIL010101ABC',
      nombre: 'Millet SA de CV',
      regimenFiscal: '601',
      lugarExpedicion: '76120',
    },
    receptor: {
      rfc: 'AAA010101AAA',
      nombre: 'Cliente Demo SA',
      regimenFiscal: '601',
      codigoPostal: '97000',
      usoCfdi: 'G03',
    },
    metodoPago: 'PUE',
    formaPago: '03',
    moneda: 'MXN',
    tipoCambio: null,
    exportacion: '01',
    serieFiscalPrevista: 'Cfdi',
    conceptos: [
      {
        posicion: 1,
        claveProdServ: '30171604',
        claveUnidad: 'H87',
        descripcion: 'Vidrio templado 6mm',
        cantidad: 2,
        valorUnitario: 3750,
        importe: 7500,
        descuento: 0,
        objetoImp: '02',
        iva: 1200,
        retencionIva: 0,
        retencionIsr: 0,
        tasaIva: 0.16,
      },
    ],
    totales: {
      subtotal: 7500,
      descuento: 0,
      impuestosTrasladados: 1200,
      retenciones: 0,
      total: 8700,
    },
  },
};

describe('Proforma PDF (U1.8a)', () => {
  it('lleva PROFORMA, SIN VALOR FISCAL, folio PF y totales; sin UUID ni sello', async () => {
    const blob = await pdf(<ProformaCfdiDocument proforma={PROFORMA} />).toBlob();
    const bytes = Buffer.from(await blob.arrayBuffer());
    const texto = textoPdf(bytes);
    const plano = texto.replace(/\s+/g, ' ');
    expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
    expect(texto).toContain('PROFORMA');
    expect(texto).toContain('SIN VALOR FISCAL');
    expect(texto).toContain('PF-MID-001-2');
    expect(texto).toContain('Vidrio templado 6mm');
    expect(texto).toContain('Cliente Demo SA');
    expect(texto).toContain('Se asigna al timbrar');
    expect(texto).toContain('Receptor sin régimen fiscal');
    expect(plano).toContain('8,700.00');
    expect(plano).toContain('7,500.00');
    expect(texto).not.toMatch(/UUID|Sello/);
    expect(texto.indexOf('Subtotal')).toBeLessThan(texto.indexOf('IVA trasladado'));
  });

  it('nombra el archivo con pedido y versión', () => {
    expect(nombreArchivoProforma(PROFORMA)).toBe('proforma-MID-001-v2.pdf');
    expect(nombreArchivoProforma({ ...PROFORMA, numeroPedido: null })).toBe(
      'proforma-PF-MID-001-2-v2.pdf',
    );
  });
});
