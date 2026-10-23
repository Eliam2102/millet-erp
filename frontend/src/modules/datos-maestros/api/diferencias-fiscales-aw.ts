import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * Cliente del reporte de diferencias fiscales A+W vs ERP (U1.8b,
 * F1-FAC-14). El backend calcula en vivo (solo lee A+W) y devuelve el
 * shape de ADR-0036 más <c>avisos</c> y <c>resumen</c>.
 */
export type TipoDiferenciaFiscal = 'Cliente' | 'Producto';
export type EstadoDiferenciaFiscal = 'Diferente' | 'Faltante' | 'SinDatoEnOrigen';

export interface FilaDiferenciaFiscal {
  tipo: TipoDiferenciaFiscal;
  tipoEtiqueta: string;
  entidadId: string;
  referencia: string;
  nombre: string;
  campo: string;
  campoEtiqueta: string;
  fuente: string;
  valorOrigen: string | null;
  valorErp: string | null;
  estado: EstadoDiferenciaFiscal;
  estadoEtiqueta: string;
  huella: string;
  revisada: boolean;
  revisadaEn: string | null;
  revisadaPor: string | null;
  nota: string | null;
}

export interface ReporteDiferenciasFiscales {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: {
    clave: string;
    etiqueta: string;
    tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
    alineacion?: string | null;
    anchoPx?: number | null;
  }[];
  filas: FilaDiferenciaFiscal[];
  totales?: Record<string, number> | null;
  avisos: string[] | null;
  resumen: {
    clientesComparados: number;
    productosComparados: number;
    pendientes: number;
    revisadas: number;
  } | null;
}

export interface CorridaDiferenciasFiscales {
  id: string;
  origen: 'Manual' | 'Programada';
  actor: string;
  iniciadaEn: string;
  terminadaEn: string | null;
  exitosa: boolean;
  clientesComparados: number;
  productosComparados: number;
  diferencias: number;
  error: string | null;
  avisos: string[];
}

export interface UltimaCorridaDiferenciasFiscales {
  corrida: CorridaDiferenciasFiscales | null;
  reporte: ReporteDiferenciasFiscales | null;
  ultimaProgramada: CorridaDiferenciasFiscales | null;
}

export interface FiltrosDiferenciasFiscales {
  tipo?: TipoDiferenciaFiscal;
  campo?: string;
  estado?: EstadoDiferenciaFiscal;
  incluirRevisadas?: boolean;
}

export const DIFERENCIAS_FISCALES_AW = '/api/v1/datos-maestros/diferencias-fiscales-aw';

/** Campos que compara el backend, en el orden en que se muestran. */
export const CAMPOS_DIFERENCIA_FISCAL: readonly { valor: string; etiqueta: string; tipo: TipoDiferenciaFiscal }[] = [
  { valor: 'RazonSocial', etiqueta: 'Razón social', tipo: 'Cliente' },
  { valor: 'Rfc', etiqueta: 'RFC', tipo: 'Cliente' },
  { valor: 'RegimenFiscal', etiqueta: 'Régimen fiscal', tipo: 'Cliente' },
  { valor: 'CodigoPostalFiscal', etiqueta: 'CP fiscal', tipo: 'Cliente' },
  { valor: 'ClaveProdServSat', etiqueta: 'Clave de producto SAT', tipo: 'Producto' },
  { valor: 'ClaveUnidadSat', etiqueta: 'Clave de unidad SAT', tipo: 'Producto' },
  { valor: 'ObjetoImp', etiqueta: 'Objeto de impuesto', tipo: 'Producto' },
];

const keys = {
  todo: ['datos-maestros', 'diferencias-fiscales-aw'] as const,
  reporte: (f: FiltrosDiferenciasFiscales) => [...keys.todo, 'reporte', f] as const,
  ultima: (incluirRevisadas: boolean) => [...keys.todo, 'ultima', incluirRevisadas] as const,
};

export function queryDiferencias(f: FiltrosDiferenciasFiscales): string {
  const params = new URLSearchParams();
  if (f.tipo) params.set('tipo', f.tipo);
  if (f.campo) params.set('campo', f.campo);
  if (f.estado) params.set('estado', f.estado);
  if (f.incluirRevisadas) params.set('incluirRevisadas', 'true');
  const qs = params.toString();
  return qs ? `${DIFERENCIAS_FISCALES_AW}?${qs}` : DIFERENCIAS_FISCALES_AW;
}

/** Reporte en vivo. <c>enabled</c> = el usuario pidió ejecutarlo (lee A+W). */
export function useDiferenciasFiscales(filtros: FiltrosDiferenciasFiscales, enabled: boolean) {
  return useQuery({
    queryKey: keys.reporte(filtros),
    queryFn: async ({ signal }) =>
      (await apiRequest<ReporteDiferenciasFiscales>(queryDiferencias(filtros), { signal })).data,
    enabled,
    staleTime: 60_000,
  });
}

export function useUltimaCorridaDiferencias(incluirRevisadas: boolean) {
  return useQuery({
    queryKey: keys.ultima(incluirRevisadas),
    queryFn: async ({ signal }) =>
      (await apiRequest<UltimaCorridaDiferenciasFiscales>(
        `${DIFERENCIAS_FISCALES_AW}/corridas/ultima${incluirRevisadas ? '?incluirRevisadas=true' : ''}`,
        { signal },
      )).data,
  });
}

export function useMarcarDiferenciaRevisada() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (fila: Pick<FilaDiferenciaFiscal, 'tipo' | 'entidadId' | 'campo' | 'huella'> & { nota?: string }) =>
      (await apiRequest(`${DIFERENCIAS_FISCALES_AW}/revisiones`, {
        method: 'POST',
        body: { tipo: fila.tipo, entidadId: fila.entidadId, campo: fila.campo, huella: fila.huella, nota: fila.nota || null },
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.todo }),
  });
}

export function useEjecutarCorridaDiferencias() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      (await apiRequest<CorridaDiferenciasFiscales>(`${DIFERENCIAS_FISCALES_AW}/corridas`, {
        method: 'POST',
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.todo }),
  });
}
