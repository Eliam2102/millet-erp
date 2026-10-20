// @vitest-environment node
import { inflateSync } from 'node:zlib';
import { writeFileSync } from 'node:fs';
import { describe, it, expect } from 'vitest';
import { pdf } from '@react-pdf/renderer';
import { BorradorCfdiDocument } from './BorradorCfdiDocument';
/** Extrae operadores de texto del PDF real (Helvetica/WinAnsi del exporter). */
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


describe('Vista previa CFDI', () => {
  it.each(['Factura', 'Anticipo', 'REP'] as const)('genera %s sin timbrar con marca y total legibles', async tipo => {
    const blob = await pdf(<BorradorCfdiDocument datos={{
      tipo, emisor: 'Empresa de pruebas · AAA010101AAA', receptor: 'Cliente ficticio · XAXX010101000',
      usoCfdi: 'G03', metodoPago: 'PUE', formaPago: '03', moneda: 'MXN',
      partidas: [{ descripcion: 'Vidrio templado', cantidad: 1, importe: 7500 }],
      importes: [{ etiqueta: 'Subtotal', monto: 7500 }, { etiqueta: 'IVA trasladado', monto: 1200 }, { etiqueta: 'Total', monto: 8700 }],
    }} />).toBlob();
    const bytes = Buffer.from(await blob.arrayBuffer());
    const texto = textoPdf(bytes);
    expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
    expect(texto).toContain('BORRADOR');
    expect(texto).toContain('sin validez fiscal');
    expect(texto.replace(/\s+/g, ' ')).toContain('8,700.00 MXN');
    expect(texto).toContain('Vidrio templado');
    expect(texto).not.toMatch(/UUID|Sello/);
    expect(texto.indexOf('Subtotal')).toBeLessThan(texto.indexOf('IVA trasladado'));
    if (process.env.F1C_PDF_OUTPUT && tipo === 'Factura') writeFileSync(process.env.F1C_PDF_OUTPUT, bytes);
  });
});
