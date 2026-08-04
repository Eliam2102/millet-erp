import {
  TipoColumnaReporteCxc,
  type ReporteCxcBackend,
} from '@/features/cxc/api/types';

/**
 * Shape canónico que consume <c>&lt;ReporteShell&gt;</c> (ADR-0036,
 * mismo contrato que <c>ReporteResponse</c> de Almacén /
 * <c>ReporteBackend</c> de Facturación).
 */
export interface ReporteShellData {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: Array<{
    clave: string;
    etiqueta: string;
    tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
    alineacion?: string | null;
    anchoPx?: number | null;
  }>;
  filas: Array<Record<string, unknown>>;
  totales?: Record<string, number> | null;
}

const TIPO_MAP: Record<TipoColumnaReporteCxc, ReporteShellData['columnas'][number]['tipo']> = {
  [TipoColumnaReporteCxc.Texto]: 'texto',
  [TipoColumnaReporteCxc.Entero]: 'numero',
  [TipoColumnaReporteCxc.Numerico]: 'numero',
  [TipoColumnaReporteCxc.Moneda]: 'moneda',
  [TipoColumnaReporteCxc.Fecha]: 'fecha',
  [TipoColumnaReporteCxc.FechaHora]: 'fecha',
  [TipoColumnaReporteCxc.Booleano]: 'texto',
  [TipoColumnaReporteCxc.Enum]: 'texto',
};

/**
 * Adapta el <c>ReporteJsonResponse</c> propio de CxC (columnas
 * <c>key/label/tipo</c> enum numérico, filtros como lista) al shape
 * canónico del <c>&lt;ReporteShell&gt;</c>. Las columnas se pasan tal
 * cual llegan — los buckets de antigüedad son CONFIGURABLES en backend
 * y el FE nunca los hardcodea.
 */
export function adaptarReporteCxc(
  reporte: ReporteCxcBackend | undefined,
): ReporteShellData | undefined {
  if (reporte == null) return undefined;

  const filtros: Record<string, string | null> = {};
  for (const f of reporte.filtrosAplicados) filtros[f.label] = f.valor;

  const totales =
    reporte.totales == null
      ? null
      : Object.fromEntries(
          Object.entries(reporte.totales).filter(
            (par): par is [string, number] => typeof par[1] === 'number',
          ),
        );

  return {
    titulo: reporte.titulo,
    generadoEn: reporte.generadoEn,
    filtrosAplicados: filtros,
    columnas: reporte.columnas.map((c) => ({
      clave: c.key,
      etiqueta: c.label,
      tipo: TIPO_MAP[c.tipo] ?? 'texto',
    })),
    filas: reporte.filas,
    totales,
  };
}
