import referencia from './qr-referencia.json';
// @vitest-environment node
import { describe, it, expect } from 'vitest';
import { renderToBuffer } from '@react-pdf/renderer';
import { writeFileSync } from 'node:fs';
import { EtiquetasPdf } from './EtiquetasPdf';
import { paginasEtiquetas } from './paginas';
import { code128, qrActivo } from './codigos';
const etiquetas = Array.from({ length: 30 }, (_, i) => ({
  id: String(i),
  version: 1,
  numero: `SAP-${String(i + 1).padStart(6, '0')}`,
  descripcion: `Equipo DEMO ${i + 1}`,
  sucursal: 'Conkal',
  empresa: 'Millet DEMO',
}));
describe('Etiquetas de activos', () => {
  it('coincide módulo por módulo con tres vectores independientes de ReportLab', () => {
    for (const caso of referencia.casos)
      expect(
        qrActivo(caso.numero).map((fila) => fila.map((v) => (v ? '1' : '0')).join('')),
      ).toEqual(caso.filas);
  });
  it('mantiene los 30 números SAP, Code128 y QR en tres hojas carta', async () => {
    const { paginas } = paginasEtiquetas(etiquetas);
    expect(paginas.map((p) => p.length)).toEqual([10, 10, 10]);
    for (const e of etiquetas) {
      expect(code128(e.numero).barras.length).toBeGreaterThan(30);
      expect(qrActivo(e.numero)).toHaveLength(33);
    }
    const buffer = await renderToBuffer(<EtiquetasPdf etiquetas={etiquetas} />);
    expect(buffer.subarray(0, 4).toString()).toBe('%PDF');
    expect(buffer.toString('latin1').match(/\/Type \/Page\b/g)).toHaveLength(3);
    if (process.env.AF_PDF_MUESTRA) writeFileSync(process.env.AF_PDF_MUESTRA, buffer);
  }, 30000);
  it('usa Code128 B con suma verificada y márgenes de silencio', () => {
    const c = code128('ABC');
    expect(c.simbolos).toEqual([104, 33, 34, 35, 1, 106]);
    expect(c.barras[0].x).toBe(10);
    expect(c.ancho).toBe(88);
  });
  it('reserva una columna para números largos y rechaza caracteres imposibles en Code128', () => {
    expect(paginasEtiquetas([{ ...etiquetas[0], numero: 'A'.repeat(60) }]).columnas).toBe(1);
    expect(() => code128('Á')).toThrow();
    expect(() => code128('A'.repeat(61))).toThrow();
    expect(qrActivo('A'.repeat(60))).toHaveLength(33);
  });
  it('incluye los patrones localizadores QR y es determinista', () => {
    const qr = qrActivo('SAP-000001');
    expect(qr[0].slice(0, 7)).toEqual(Array(7).fill(true));
    expect(qr[1].slice(0, 7)).toEqual([true, false, false, false, false, false, true]);
    expect(qr[26][26]).toBe(true);
    expect(qrActivo('SAP-000001')).toEqual(qr);
  });
});
