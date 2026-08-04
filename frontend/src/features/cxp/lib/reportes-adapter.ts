import type { BackendReporteResponse } from '@/features/cxp/api/useReportes';

/**
 * Mapea el shape backend (PascalCase serializado a camelCase) al shape
 * que <c>&lt;ReporteShell&gt;</c> consume. El backend usa enums
 * numéricos para tipo de columna; ReporteShell usa uniones string.
 *
 * <para>Backend <c>TipoColumnaReporte</c>: 1=Texto, 2=Entero,
 * 3=Numerico, 4=Moneda, 5=Fecha, 6=FechaHora, 7=Booleano, 8=Enum.</para>
 *
 * <para>ReporteShell <c>tipo</c>: 'texto' | 'numero' | 'moneda' | 'fecha'.</para>
 */

type ShellTipo = 'texto' | 'numero' | 'moneda' | 'fecha';

function mapTipo(tipo: number): ShellTipo {
  switch (tipo) {
    case 4:
      return 'moneda';
    case 2:
    case 3:
      return 'numero';
    case 5:
    case 6:
      return 'fecha';
    default:
      return 'texto';
  }
}

export interface ReporteShellShape {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: {
    clave: string;
    etiqueta: string;
    tipo: ShellTipo;
    alineacion: string | null;
    anchoPx: number | null;
  }[];
  filas: Record<string, unknown>[];
  totales: Record<string, number> | null;
}

export function backendToShellShape(
  data: BackendReporteResponse,
): ReporteShellShape {
  const filtrosAplicados: Record<string, string | null> = {};
  for (const f of data.filtrosAplicados) {
    filtrosAplicados[f.label] = f.valor;
  }

  // Totales del backend pueden venir mixtos (numéricos + strings); filtrar
  // a sólo valores numéricos para que ReporteShell los formatee con moneda/numero.
  const totales: Record<string, number> = {};
  if (data.totales) {
    for (const [k, v] of Object.entries(data.totales)) {
      if (typeof v === 'number') totales[k] = v;
    }
  }

  return {
    titulo: data.titulo,
    generadoEn: data.generadoEn,
    filtrosAplicados,
    columnas: data.columnas.map((c) => ({
      clave: c.key,
      etiqueta: c.label,
      tipo: mapTipo(c.tipo),
      alineacion: null,
      anchoPx: null,
    })),
    filas: data.filas,
    totales: Object.keys(totales).length > 0 ? totales : null,
  };
}
