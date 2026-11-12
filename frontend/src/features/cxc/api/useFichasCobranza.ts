import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

const BASE = '/api/v1/cuentas-por-cobrar/cobranza/fichas';
const clavesFichas = ['cxc', 'cobranza', 'fichas'] as const;

/** M-241: espejo de <c>FichaFacturaVencidaDto</c> (backend). */
export interface FichaFacturaVencida {
  facturaCarteraId: string;
  clienteId: string;
  clienteNombre: string;
  folio: string;
  moneda: string;
  saldo: number;
  fechaVencimiento: string;
  diasVencida: number;
  fichaId: string | null;
  responsableId: string | null;
  responsableNombre: string | null;
  siguienteRevision: string | null;
  escalada: boolean;
  revisionVencida: boolean;
  cerrada: boolean;
}

/** M-241: espejo de <c>FichaCobranzaEventoResponse</c> (backend). */
export interface FichaCobranzaEvento {
  id: string;
  tipo: number;
  tipoNombre: 'Asignacion' | 'Gestion' | 'Promesa' | 'Reprogramacion' | 'Escalamiento' | 'Correccion' | 'Cierre';
  fecha: string;
  usuarioNombre: string | null;
  nota: string;
  canal: number | null;
  resultado: number | null;
  montoComprometido: number | null;
  fechaComprometida: string | null;
  revisionAntes: string | null;
  revisionDespues: string | null;
  responsableAntes: string | null;
  responsableDespues: string | null;
  corrigeEventoId: string | null;
  notaAnterior: string | null;
  motivoCorreccion: string | null;
  /** M-345: solo en promesas. */
  estadoPromesa: 'Vigente' | 'Cumplida' | 'Parcial' | 'Incumplida' | null;
  pagadoPromesa: number | null;
  faltantePromesa: number | null;
}

export interface FiltrosFichas {
  clienteId?: string | null;
  soloSinResponsable?: boolean;
}

export function useFichasCobranza(filtros: FiltrosFichas) {
  return useQuery<FichaFacturaVencida[]>({
    queryKey: [...clavesFichas, 'lista', filtros.clienteId ?? 'todos', filtros.soloSinResponsable ?? false],
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.soloSinResponsable) params.set('soloSinResponsable', 'true');
      const { data } = await apiRequest<FichaFacturaVencida[]>(`${BASE}?${params}`, { signal });
      return data;
    },
  });
}

export function useEventosFicha(facturaCarteraId: string | null) {
  return useQuery<FichaCobranzaEvento[]>({
    queryKey: [...clavesFichas, 'eventos', facturaCarteraId ?? ''],
    enabled: facturaCarteraId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<FichaCobranzaEvento[]>(`${BASE}/${facturaCarteraId}/eventos`, { signal });
      return data;
    },
  });
}

function useAccionFicha<TArgs>(ruta: (a: TArgs) => string, cuerpo: (a: TArgs) => unknown) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: TArgs) => {
      const { data } = await apiRequest<unknown>(ruta(args), {
        method: 'POST',
        body: JSON.stringify(cuerpo(args)),
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: clavesFichas }),
  });
}

/** Cuerpo sin el identificador que ya va en la ruta. */
function sinCampo<T extends object, K extends keyof T>(obj: T, campo: K): Omit<T, K> {
  return Object.fromEntries(Object.entries(obj).filter(([k]) => k !== campo)) as Omit<T, K>;
}

export const useAsignarResponsableFicha = () =>
  useAccionFicha<{ facturaCarteraId: string; responsableId: string; responsableNombre: string | null; siguienteRevision?: string; motivo?: string }>(
    (a) => `${BASE}/${a.facturaCarteraId}/responsable`,
    (a) => sinCampo(a, 'facturaCarteraId'),
  );

export const useRegistrarGestionFicha = () =>
  useAccionFicha<{
    facturaCarteraId: string;
    canal: number;
    resultado: number;
    nota: string;
    montoComprometido?: number;
    fechaComprometida?: string;
    siguienteRevision?: string;
  }>((a) => `${BASE}/${a.facturaCarteraId}/gestion`, (a) => sinCampo(a, 'facturaCarteraId'));

export const useReprogramarFicha = () =>
  useAccionFicha<{ facturaCarteraId: string; siguienteRevision: string; motivo: string }>(
    (a) => `${BASE}/${a.facturaCarteraId}/reprogramar`,
    (a) => sinCampo(a, 'facturaCarteraId'),
  );

export const useEscalarFicha = () =>
  useAccionFicha<{ facturaCarteraId: string; supervisorId: string; supervisorNombre: string | null; motivo: string }>(
    (a) => `${BASE}/${a.facturaCarteraId}/escalar`,
    (a) => sinCampo(a, 'facturaCarteraId'),
  );

export const useCorregirEventoFicha = () =>
  useAccionFicha<{ eventoId: string; notaCorregida: string; motivo: string }>(
    (a) => `${BASE}/eventos/${a.eventoId}/corregir`,
    (a) => sinCampo(a, 'eventoId'),
  );

/** M-345: motivos de cierre de la ficha (espejo de <c>MotivoCierreFicha</c>). */
export const MOTIVOS_CIERRE_FICHA = [
  { valor: 1, etiqueta: 'Pagada' },
  { valor: 2, etiqueta: 'Acuerdo de pago' },
  { valor: 3, etiqueta: 'Disputa' },
  { valor: 4, etiqueta: 'Otro' },
] as const;

export const useCerrarFicha = () =>
  useAccionFicha<{ facturaCarteraId: string; motivo: number; explicacionSaldo?: string }>(
    (a) => `${BASE}/${a.facturaCarteraId}/cerrar`,
    (a) => sinCampo(a, 'facturaCarteraId'),
  );
