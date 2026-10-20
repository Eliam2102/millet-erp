// @vitest-environment node
import { inflateSync } from 'node:zlib';
import { writeFileSync } from 'node:fs';
import { describe, expect, it, vi } from 'vitest';
import { descargarReportePdf } from './reporte-pdf-exporter';
import type { CorteCajaFila } from '@/features/facturacion/api/types';

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

describe('PDF de corte de caja', () => {
  it('descarga una tabla con texto de una celda seleccionable, no una imagen', async () => {
    let generado: Blob | undefined;
    const click = vi.fn();
    vi.stubGlobal('document', {
      createElement: () => ({ click }),
      body: { appendChild: vi.fn(), removeChild: vi.fn() },
    });
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      generado = blob as Blob;
      return 'blob:corte-prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    try {
      await descargarReportePdf<Partial<CorteCajaFila>>(
        {
          titulo: 'Corte de caja - muestra F1',
          generadoEn: '2026-10-19T12:00:00Z',
          filtrosAplicados: {},
          columnas: [
            { clave: 'comprobante', etiqueta: 'Comprobante', tipo: 'texto' },
            { clave: 'importe', etiqueta: 'Importe', tipo: 'moneda' },
          ],
          filas: [{ comprobante: 'CELDA CORTE F1', importe: 1044 }],
          totales: { importe: 1044 },
        },
        'corte-f1',
      );
      expect(click).toHaveBeenCalledOnce();
      const bytes = Buffer.from(await generado!.arrayBuffer());
      expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
      expect(textoPdf(bytes)).toContain('CELDA CORTE F1');
      expect(textoPdf(bytes)).toContain('1,044.00');
      expect(bytes.toString('latin1')).not.toMatch(/\/Subtype\s*\/Image/);
      if (process.env.F1_PDF_OUTPUT) writeFileSync(process.env.F1_PDF_OUTPUT, bytes);
    } finally {
      vi.restoreAllMocks();
      vi.unstubAllGlobals();
    }
  });
});
