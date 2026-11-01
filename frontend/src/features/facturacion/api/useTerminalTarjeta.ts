import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** 86e3vr1tf: estado de una venta de la terminal frente a los cobros de caja. */
export const EstadoTransaccionTerminal = { Emparejada: 1, SinEmparejar: 2, Rechazada: 3 } as const;
export type EstadoTransaccionTerminal =
  (typeof EstadoTransaccionTerminal)[keyof typeof EstadoTransaccionTerminal];

export interface CargaTerminal {
  id: string;
  nombreArchivo: string;
  fechaArchivo: string;
  cargadoEn: string;
  renglones: number;
  nuevos: number;
  duplicados: number;
  emparejados: number;
  sinEmparejar: number;
  rechazados: number;
  liquidaciones: number;
}

export interface TransaccionTerminal {
  id: string;
  noOperacion: string;
  fechaHora: string;
  importe: number;
  moneda: string;
  ultimos4: string | null;
  autorizacion: string | null;
  estatus: string;
  estado: EstadoTransaccionTerminal;
  comision: number | null;
  ivaComision: number | null;
  neto: number | null;
  folioLiquidacion: string | null;
  cobroMostradorId: string | null;
  folioComprobante: string | null;
  liquidacionId: string | null;
}

interface Pagina<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

const BASE = '/api/v1/facturacion/terminal-tarjetas';
const keys = {
  all: ['facturacion', 'terminal-tarjetas'] as const,
  cargas: () => [...keys.all, 'cargas'] as const,
  transacciones: (estado: number | undefined) => [...keys.all, 'transacciones', { estado }] as const,
};

export function useCargasTerminal(opts?: { enabled?: boolean }) {
  return useQuery<CargaTerminal[]>({
    queryKey: keys.cargas(),
    enabled: opts?.enabled ?? true,
    queryFn: async ({ signal }) => (await apiRequest<CargaTerminal[]>(`${BASE}/cargas?limit=5`, { signal })).data,
  });
}

export function useTransaccionesTerminal(estado: number | undefined, opts?: { enabled?: boolean }) {
  return useQuery<Pagina<TransaccionTerminal>>({
    queryKey: keys.transacciones(estado),
    enabled: opts?.enabled ?? true,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<Pagina<TransaccionTerminal>>(
          `${BASE}/transacciones?limit=50${estado != null ? `&estado=${estado}` : ''}`,
          { signal },
        )
      ).data,
  });
}

/** Sube el archivo diario de la terminal; recargarlo no duplica renglones. */
export function useCargarArchivoTerminal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { archivo: File; fechaArchivo: string; idempotencyKey: string }) => {
      const formData = new FormData();
      formData.append('archivo', args.archivo);
      formData.append('fechaArchivo', args.fechaArchivo);
      return (
        await apiRequest<CargaTerminal>(`${BASE}/cargas`, {
          method: 'POST',
          body: formData,
          idempotencyKey: args.idempotencyKey,
        })
      ).data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

/** Reintenta el emparejamiento de las ventas sin cobro (después de registrarlo). */
export function useReemparejarTerminal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { idempotencyKey: string }) =>
      (await apiRequest<CargaTerminal>(`${BASE}/reemparejar`, { method: 'POST', idempotencyKey: args.idempotencyKey }))
        .data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}
