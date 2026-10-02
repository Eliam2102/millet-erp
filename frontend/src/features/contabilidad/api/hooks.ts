import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type {
  Cuenta,
  CuentaBody,
  CuerpoImportacion,
  FiltroEstatus,
  FiltrosLista,
  NodoArbol,
  PaginaCuentas,
  Perfil,
  ResultadoAplicar,
  SiguienteCodigo,
  VistaPrevia,
} from './types';

const BASE = '/api/v1/contabilidad';

export const contabKeys = {
  all: ['contabilidad'] as const,
  arbol: (raizId: string | null, estatus: FiltroEstatus) =>
    ['contabilidad', 'arbol', raizId, estatus] as const,
  lista: (f: FiltrosLista) => ['contabilidad', 'lista', f] as const,
  detalle: (id: string) => ['contabilidad', 'detalle', id] as const,
  ancestros: (id: string, padreId: string | null) =>
    ['contabilidad', 'ancestros', id, padreId] as const,
  siguienteCodigo: (padreId: string) => ['contabilidad', 'siguiente-codigo', padreId] as const,
};

function qs(params: Record<string, unknown>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '' && v !== false) p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : '';
}

/** Hijas directas de `raizId` (null = raíces) — carga perezosa. */
export function useArbol(raizId: string | null, estatus: FiltroEstatus) {
  return useQuery({
    queryKey: contabKeys.arbol(raizId, estatus),
    queryFn: async ({ signal }) =>
      (await apiRequest<NodoArbol[]>(`${BASE}/cuentas/arbol${qs({ raizId, estatus })}`, { signal })).data,
  });
}

/** Código sugerido para una hija nueva del padre (solo al crear). Sin padre (raíz) no se consulta. */
export function useSiguienteCodigo(padreId: string | null, enabled = true) {
  return useQuery({
    queryKey: contabKeys.siguienteCodigo(padreId ?? ''),
    enabled: enabled && !!padreId,
    staleTime: 0,
    queryFn: async ({ signal }) =>
      (await apiRequest<SiguienteCodigo>(`${BASE}/cuentas/siguiente-codigo${qs({ padreId })}`, { signal })).data,
  });
}

export function useCuentas(filtros: FiltrosLista, enabled = true) {
  return useQuery({
    queryKey: contabKeys.lista(filtros),
    enabled,
    queryFn: async ({ signal }) =>
      (await apiRequest<PaginaCuentas>(`${BASE}/cuentas${qs({ ...filtros })}`, { signal })).data,
  });
}

interface DetalleCacheado {
  data: Cuenta;
  etag?: string;
}

/** GET por id: cachea `{data, etag}` para el If-Match de las mutaciones (ADR-0012). */
export function useCuenta(id: string | null) {
  return useQuery({
    queryKey: contabKeys.detalle(id ?? 'none'),
    enabled: id !== null,
    queryFn: async ({ signal }): Promise<DetalleCacheado> => {
      const { data, etag } = await apiRequest<Cuenta>(`${BASE}/cuentas/${id}`, { signal });
      return { data, etag };
    },
  });
}

/** Cadena de ancestros (raíz → padre) resuelta con el GET por id (el DTO no la trae). */
export function useAncestros(cuenta: Cuenta | undefined) {
  return useQuery({
    queryKey: contabKeys.ancestros(cuenta?.id ?? 'none', cuenta?.padreId ?? null),
    enabled: cuenta !== undefined,
    queryFn: async ({ signal }) => {
      const cadena: Cuenta[] = [];
      let padreId = cuenta!.padreId;
      while (padreId && cadena.length < 12) {
        const { data } = await apiRequest<Cuenta>(`${BASE}/cuentas/${padreId}`, { signal });
        cadena.unshift(data);
        padreId = data.padreId;
      }
      return cadena;
    },
  });
}

function useInvalidar() {
  const qc = useQueryClient();
  return () => void qc.invalidateQueries({ queryKey: contabKeys.all });
}

export function useCrearCuenta() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (body: CuentaBody) =>
      (await apiRequest<Cuenta>(`${BASE}/cuentas`, {
        method: 'POST',
        body,
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: invalidar,
  });
}

function useIfMatch() {
  const qc = useQueryClient();
  return (id: string): string | undefined => {
    const c = qc.getQueryData<DetalleCacheado>(contabKeys.detalle(id));
    return c?.etag ?? (c ? String(c.data.version) : undefined);
  };
}

export function useEditarCuenta() {
  const invalidar = useInvalidar();
  const ifMatch = useIfMatch();
  return useMutation({
    mutationFn: async (a: { id: string; body: CuentaBody }) =>
      (await apiRequest<Cuenta>(`${BASE}/cuentas/${a.id}`, {
        method: 'PUT',
        body: a.body,
        idempotencyKey: crypto.randomUUID(),
        ifMatch: ifMatch(a.id),
      })).data,
    onSuccess: invalidar,
  });
}

export function useCambiarEstatusCuenta() {
  const invalidar = useInvalidar();
  const ifMatch = useIfMatch();
  return useMutation({
    mutationFn: async (a: { id: string; accion: 'desactivar' | 'reactivar' }) =>
      (await apiRequest<Cuenta>(`${BASE}/cuentas/${a.id}/${a.accion}`, {
        method: 'POST',
        idempotencyKey: crypto.randomUUID(),
        ifMatch: ifMatch(a.id),
      })).data,
    onSuccess: invalidar,
  });
}

// ─── Importación ─────────────────────────────────────────────────────────────

export function usePerfilado() {
  return useMutation({
    mutationFn: async (cuerpo: CuerpoImportacion) =>
      (await apiRequest<Perfil>(`${BASE}/importaciones/perfilado`, { method: 'POST', body: cuerpo })).data,
  });
}

export function useVistaPrevia() {
  return useMutation({
    mutationFn: async (cuerpo: CuerpoImportacion) =>
      (await apiRequest<VistaPrevia>(`${BASE}/importaciones/vista-previa`, { method: 'POST', body: cuerpo })).data,
  });
}

/** La Idempotency-Key la fija el caller por archivo: reintentar el mismo archivo reusa la misma clave. */
export function useAplicarImportacion() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { cuerpo: CuerpoImportacion; idempotencyKey: string }) =>
      (await apiRequest<ResultadoAplicar>(`${BASE}/importaciones`, {
        method: 'POST',
        body: a.cuerpo,
        idempotencyKey: a.idempotencyKey,
      })).data,
    onSuccess: invalidar,
  });
}
