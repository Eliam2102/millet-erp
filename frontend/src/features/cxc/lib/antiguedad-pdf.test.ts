import { describe, expect, it } from 'vitest';
import { conFormatoPdfAntiguedad, LETRA_PDF_ANTIGUEDAD } from '@/features/cxc/lib/antiguedad-pdf';
import type { ReporteShellData } from '@/features/cxc/lib/reporte-cxc-adapter';

const texto = (clave: string) => ({ clave, etiqueta: clave, tipo: 'texto' as const });
const importe = (clave: string) => ({ clave, etiqueta: clave, tipo: 'moneda' as const });

const reporte: ReporteShellData = {
  titulo: 'Antigüedad de saldos de cartera',
  generadoEn: '2026-11-07T12:00:00Z',
  filtrosAplicados: {},
  columnas: [
    texto('rfc'),
    texto('cliente'),
    texto('clasificacion'),
    texto('moneda'),
    importe('por_vencer'),
    importe('d1_15'),
    importe('d16_30'),
    importe('d31_60'),
    importe('d61_90'),
    importe('d90_mas'),
    importe('vencido'),
    importe('total'),
    { clave: 'tipo_cambio', etiqueta: 'TC al corte', tipo: 'numero' },
    importe('vencido_mxn'),
    importe('total_mxn'),
    { clave: 'numero_facturas', etiqueta: '# Facturas', tipo: 'numero' },
  ],
  filas: [],
};

/** Ancho útil de la tabla: carta horizontal (792 pt) menos 24 pt de margen por lado. */
const ANCHO_TABLA = 792 - 48;
/** «$187,147.00» en Helvetica a 7 pt: 9 caracteres de 0.556 em y 2 de 0.278 em, más 8 pt de relleno. */
const ANCHO_IMPORTE_PT = (9 * 0.556 + 2 * 0.278) * LETRA_PDF_ANTIGUEDAD + 8;

describe('conFormatoPdfAntiguedad (O1)', () => {
  it('con 16 columnas los importes caben en su columna y el RFC y la clasificación se cortan por carácter', () => {
    const r = conFormatoPdfAntiguedad(reporte)!;
    const pesos = r.columnas.map((c) => c.anchoPdf ?? 1);
    const unidad = ANCHO_TABLA / pesos.reduce((s, p) => s + p, 0);

    expect(r.letraPdf).toBe(7);
    for (const c of r.columnas.filter((c) => c.tipo === 'moneda')) {
      expect((c.anchoPdf ?? 1) * unidad).toBeGreaterThanOrEqual(ANCHO_IMPORTE_PT);
    }
    expect(r.columnas.filter((c) => c.cortePdf === 'caracter').map((c) => c.clave)).toEqual(['rfc', 'clasificacion']);
    expect(r.columnas.find((c) => c.clave === 'cliente')?.cortePdf).toBeNull();
  });

  it('sin reporte no hay nada que formatear', () => {
    expect(conFormatoPdfAntiguedad(undefined)).toBeUndefined();
  });
});
