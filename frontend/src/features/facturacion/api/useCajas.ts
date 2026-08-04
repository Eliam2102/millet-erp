import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  CajaDetalleResponse,
  CajaMutadaResponse,
  CajaListadoItem,
  UsuarioAlcanceInput,
  UsuarioAlcanceItem,
  UsuarioAlcancesResponse,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/cajas';

/** Entrada de cache del detalle: DTO + ETag para el If-Match de las mutaciones. */
interface CachedCaja {
  data: CajaDetalleResponse;
  etag: string | undefined;
}

/** <c>useListarCajas(soloActivas)</c> — bandeja de cajas (CAJAS-PR5). */
export function useListarCajas(soloActivas = false) {
  return useQuery<CajaListadoItem[]>({
    queryKey: facturacionKeys.cajasList({ soloActivas }),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CajaListadoItem[]>(
        soloActivas ? `${BASE}?soloActivas=true` : `${BASE}/`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useCaja(id)</c> — detalle con alcances. Guarda `{ data, etag }` en cache
 * (el componente ve solo el DTO vía `select`); las mutaciones leen el ETag
 * con `getQueryData` para el If-Match (ADR-0012).
 */
export function useCaja(id: string | null | undefined) {
  return useQuery({
    queryKey: id != null ? facturacionKeys.cajaById(id) : ['facturacion', 'noop-caja'],
    queryFn: async ({ signal }): Promise<CachedCaja> => {
      if (id == null) throw new Error('useCaja invocado sin id');
      const { data, etag } = await apiRequest<CajaDetalleResponse>(`${BASE}/${id}`, {
        signal,
      });
      return { data, etag };
    },
    enabled: id != null,
    select: (c: CachedCaja) => c.data,
    staleTime: 0,
  });
}

/** <c>useCrearCaja()</c> — POST con Idempotency-Key; nace activa y sin alcance. */
export function useCrearCaja() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: { nombre: string; descripcion: string | null };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CajaMutadaResponse>(`${BASE}/`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.cajas() });
    },
  });
}

function useMutacionConIfMatch<TBody>(sufijo: '' | 'sucursales' | 'canales' | 'usuarios') {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; body: TBody }) => {
      const cached = queryClient.getQueryData<CachedCaja>(
        facturacionKeys.cajaById(args.id),
      );
      // El ETag del header es un mirror de Version; si no llegó (p. ej.
      // proxy que filtra el header), caemos al version del DTO — el
      // backend exige If-Match (428 si falta).
      const ifMatch =
        cached?.etag ?? (cached != null ? String(cached.data.version) : undefined);
      const { data } = await apiRequest<CajaMutadaResponse>(
        sufijo === '' ? `${BASE}/${args.id}` : `${BASE}/${args.id}/${sufijo}`,
        { method: 'PUT', body: args.body, ifMatch },
      );
      return data;
    },
    onSuccess: (_data, variables) => {
      // Cada mutación incrementa Version: refresca el detalle (nuevo ETag)
      // y la bandeja (counts).
      void queryClient.invalidateQueries({
        queryKey: facturacionKeys.cajaById(variables.id),
      });
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.cajas() });
    },
  });
}

/** <c>useActualizarCaja()</c> — PUT datos generales + estatus (If-Match). */
export function useActualizarCaja() {
  return useMutacionConIfMatch<{
    nombre: string;
    descripcion: string | null;
    activa: boolean;
  }>('');
}

/** Replace-set de sucursales del alcance (If-Match). */
export function useReemplazarSucursalesCaja() {
  return useMutacionConIfMatch<{ sucursalIds: string[] }>('sucursales');
}

/** Replace-set de canales de venta del alcance (If-Match). */
export function useReemplazarCanalesCaja() {
  return useMutacionConIfMatch<{ canalVentaIds: number[] }>('canales');
}

/** Replace-set de cajeros relacionados (If-Match). */
export function useReemplazarUsuariosCaja() {
  return useMutacionConIfMatch<{ usuarioIds: string[] }>('usuarios');
}

/** Concesiones de alcance de usuarios sin caja ([Decisión 12-6]). */
export function useUsuarioAlcances(usuarioId: string | null) {
  return useQuery<UsuarioAlcanceItem[]>({
    queryKey: facturacionKeys.usuarioAlcancesList(usuarioId),
    queryFn: async ({ signal }) => {
      const params = usuarioId != null ? `?usuarioId=${usuarioId}` : '';
      const { data } = await apiRequest<UsuarioAlcanceItem[]>(
        `${BASE}/usuario-alcances${params}`,
        { signal },
      );
      return data;
    },
  });
}

/** Replace-set de concesiones de un usuario (PUT, sin If-Match — reemplazo total). */
export function useReemplazarUsuarioAlcances() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { usuarioId: string; alcances: UsuarioAlcanceInput[] }) => {
      const { data } = await apiRequest<UsuarioAlcancesResponse>(
        `${BASE}/usuario-alcances/${args.usuarioId}`,
        { method: 'PUT', body: { alcances: args.alcances } },
      );
      return data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: facturacionKeys.usuarioAlcances(),
      });
    },
  });
}
