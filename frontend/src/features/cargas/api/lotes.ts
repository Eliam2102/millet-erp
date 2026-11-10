import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** M-047 + M-167 (86e3jvvmf): lote común de cargas. Mirror de <c>LotesCargaServicio</c>. */
const BASE = '/api/v1/cargas';

export interface ColumnaTipoCarga { nombre: string; ayuda: string; obligatoria: boolean; alias: string[] }
export interface ParametroCarga {
  nombre: string; etiqueta: string; ayuda: string; obligatorio: boolean; porOmision: string | null;
  opciones?: { valor: string; etiqueta: string }[] | null;
}
export interface TipoCarga {
  tipo: string; nombre: string; columnas: ColumnaTipoCarga[]; requiereFechaCorte: boolean;
  puedeSubir: boolean; puedeAutorizar: boolean; parametros?: ParametroCarga[];
}
export interface EncabezadosArchivo { encabezados: string[]; mapeoSugerido: Record<string, string> }
export type EstadoLoteCarga = 'Subido' | 'Autorizado' | 'Rechazado' | 'Aplicado';
export interface LoteCargaResumen {
  id: string; tipo: string; nombreArchivo: string; estado: EstadoLoteCarga; fechaCorte: string | null;
  totalFilas: number; totalErrores: number; puedeAplicar: boolean; subidoPor: string; subidoEn: string;
  decididoPor: string | null; decididoEn: string | null; folioResultado: string | null; ultimoError: string | null;
}
export interface ErrorCarga { fila: number | null; campo: string; codigo: string; mensaje: string }
export interface FilaLote { fila: number; valores: Record<string, string>; errores: ErrorCarga[] }
export interface HistorialLote { accion: string; usuario: string; en: string; detalle: string | null }
export interface LoteCargaDetalle {
  lote: LoteCargaResumen; huella: string; mapeo: Record<string, string>; parametros?: Record<string, string | null>;
  resumen: Record<string, string>;
  erroresArchivo: ErrorCarga[]; filas: FilaLote[]; filasMostradas: number; motivoRechazo: string | null;
  referencia: string | null; historial: HistorialLote[]; puedeAutorizar: boolean; puedeRechazar: boolean; puedeReintentar: boolean;
}

export const cargasKeys = {
  tipos: ['cargas', 'tipos'] as const,
  lotes: (tipo: string) => ['cargas', 'lotes', tipo] as const,
  lote: (id: string) => ['cargas', 'lote', id] as const,
};

export function useTiposCarga() {
  return useQuery({
    queryKey: cargasKeys.tipos,
    queryFn: async () => (await apiRequest<TipoCarga[]>(`${BASE}/tipos`)).data,
  });
}

export function useLotesCarga(tipo: string, habilitado = true) {
  return useQuery({
    queryKey: cargasKeys.lotes(tipo),
    enabled: habilitado,
    queryFn: async () => (await apiRequest<LoteCargaResumen[]>(`${BASE}/tipos/${encodeURIComponent(tipo)}/lotes`)).data,
  });
}

export function useLoteCarga(id: string | null) {
  return useQuery({
    queryKey: cargasKeys.lote(id ?? ''),
    enabled: id != null,
    queryFn: async () => (await apiRequest<LoteCargaDetalle>(`${BASE}/lotes/${id}`)).data,
  });
}

export function useEncabezadosArchivo(tipo: string) {
  return useMutation({
    mutationFn: async (archivo: File) => {
      const body = new FormData();
      body.append('archivo', archivo, archivo.name);
      return (await apiRequest<EncabezadosArchivo>(`${BASE}/tipos/${encodeURIComponent(tipo)}/encabezados`, { method: 'POST', body })).data;
    },
  });
}

export function useSubirLote(tipo: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      archivo: File; fechaCorte: string | null; mapeo: Record<string, string>; parametros?: Record<string, string>;
    }) => {
      const body = new FormData();
      body.append('archivo', args.archivo, args.archivo.name);
      if (args.fechaCorte) body.append('fechaCorte', args.fechaCorte);
      body.append('mapeo', JSON.stringify(args.mapeo));
      if (args.parametros) body.append('parametros', JSON.stringify(args.parametros));
      return (await apiRequest<LoteCargaDetalle>(`${BASE}/tipos/${encodeURIComponent(tipo)}/lotes`,
        { method: 'POST', body, idempotencyKey: crypto.randomUUID() })).data;
    },
    onSuccess: (d) => {
      qc.setQueryData(cargasKeys.lote(d.lote.id), d);
      void qc.invalidateQueries({ queryKey: cargasKeys.lotes(tipo) });
    },
  });
}

export function useAccionLote(tipo: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; accion: 'autorizar' | 'rechazar' | 'reintentar'; motivo?: string }) =>
      (await apiRequest<LoteCargaDetalle>(`${BASE}/lotes/${args.id}/${args.accion}`, {
        method: 'POST',
        body: args.accion === 'rechazar' ? { motivo: args.motivo ?? '' } : undefined,
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: (d) => {
      qc.setQueryData(cargasKeys.lote(d.lote.id), d);
      void qc.invalidateQueries({ queryKey: cargasKeys.lotes(tipo) });
    },
  });
}
