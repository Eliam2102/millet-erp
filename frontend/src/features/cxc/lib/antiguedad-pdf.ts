import type { ReporteShellData } from './reporte-cxc-adapter';

/**
 * Antigüedad de saldos en PDF (O1, CXC-11). Con el TC al corte y los equivalentes en MXN el reporte llega a 16 columnas; a
 * 9 pt y con el mismo ancho para todas, el RFC se encimaba con el cliente y los importes se pegaban. En el PDF la tabla va a
 * 7 pt y cada columna lleva su peso: los importes, lo necesario para «$1,234,567.89»; los textos cortos, menos. El RFC y la
 * clasificación son una sola palabra larga y pueden cortarse por carácter en vez de salirse de su columna.
 */
const ANCHO_PDF: Record<string, number> = {
  rfc: 1.3,
  cliente: 1.5,
  clasificacion: 0.8,
  moneda: 0.7,
  tipo_cambio: 0.75,
  numero_facturas: 0.75,
};

/** Peso de los importes (por vencer, rangos, vencido, total y sus equivalentes en MXN). */
const ANCHO_IMPORTE = 1;

const CORTE_POR_CARACTER = new Set(['rfc', 'clasificacion']);

/** Letra de la tabla del PDF cuando el reporte trae muchas columnas. */
export const LETRA_PDF_ANTIGUEDAD = 7;

export function conFormatoPdfAntiguedad(reporte: ReporteShellData | undefined): ReporteShellData | undefined {
  if (!reporte) return undefined;
  return {
    ...reporte,
    letraPdf: LETRA_PDF_ANTIGUEDAD,
    columnas: reporte.columnas.map((c) => ({
      ...c,
      anchoPdf: ANCHO_PDF[c.clave] ?? (c.tipo === 'moneda' ? ANCHO_IMPORTE : null),
      cortePdf: CORTE_POR_CARACTER.has(c.clave) ? ('caracter' as const) : null,
    })),
  };
}
