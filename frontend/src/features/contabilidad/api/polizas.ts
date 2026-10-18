import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type {
  AnularPolizaRequest,
  FiltrosPolizas,
  PaginaPolizas,
  Poliza,
  PolizaManualRequest,
  SaldoCuentaPeriodo,
} from './polizas-types';

/** Pólizas contables (F1-CON-04). Las mutaciones llevan Idempotency-Key y, sobre una póliza existente, If-Match con su versión. */
const BASE = '/api/v1/contabilidad/polizas';

export const polizaKeys = {
  all: ['contabilidad', 'polizas'] as const,
  lista: (f: FiltrosPolizas) => ['contabilidad', 'polizas', 'lista', f] as const,
  detalle: (id: string) => ['contabilidad', 'polizas', 'detalle', id] as const,
  saldos: (anio: number, numero: number, cuentaId: string) => ['contabilidad', 'polizas', 'saldos', anio, numero, cuentaId] as const,
};

function qs(params: Record<string, unknown>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : '';
}

function useInvalidar() {
  const qc = useQueryClient();
  return () => void qc.invalidateQueries({ queryKey: polizaKeys.all });
}

export function usePolizas(filtros: FiltrosPolizas) {
  return useQuery({
    queryKey: polizaKeys.lista(filtros),
    queryFn: async ({ signal }) => (await apiRequest<PaginaPolizas>(`${BASE}${qs({ ...filtros })}`, { signal })).data,
  });
}

export function usePoliza(id: string | null) {
  return useQuery({
    queryKey: polizaKeys.detalle(id ?? 'none'),
    enabled: !!id,
    queryFn: async ({ signal }) => (await apiRequest<Poliza>(`${BASE}/${id}`, { signal })).data,
  });
}

/** Movimientos del periodo por cuenta (autorizadas, anuladas y sus anulaciones; sin borradores). */
export function useSaldosPeriodo(anio: number | null, numero: number | null, cuentaId = '') {
  return useQuery({
    queryKey: polizaKeys.saldos(anio ?? 0, numero ?? 0, cuentaId),
    enabled: !!anio && !!numero,
    queryFn: async ({ signal }) =>
      (await apiRequest<SaldoCuentaPeriodo[]>(`${BASE}/saldos${qs({ anio, numero, cuentaId })}`, { signal })).data,
  });
}

export function useCrearPoliza() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { body: PolizaManualRequest; idempotencyKey: string }) =>
      (await apiRequest<Poliza>(BASE, { method: 'POST', body: a.body, idempotencyKey: a.idempotencyKey })).data,
    onSuccess: invalidar,
  });
}

export function useEditarPoliza() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { id: string; version: number; body: PolizaManualRequest; idempotencyKey: string }) =>
      (await apiRequest<Poliza>(`${BASE}/${a.id}`, {
        method: 'PUT', body: a.body, idempotencyKey: a.idempotencyKey, ifMatch: String(a.version),
      })).data,
    onSuccess: invalidar,
  });
}

/** Descarta un borrador (no tiene número: no deja hueco en el consecutivo). */
export function useDescartarPoliza() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { poliza: Poliza; idempotencyKey: string }) =>
      (await apiRequest<void>(`${BASE}/${a.poliza.id}`, {
        method: 'DELETE', idempotencyKey: a.idempotencyKey, ifMatch: String(a.poliza.version),
      })).data,
    onSuccess: (_d, a) => {
      qc.removeQueries({ queryKey: polizaKeys.detalle(a.poliza.id) });
      void qc.invalidateQueries({ queryKey: polizaKeys.all });
    },
  });
}

/** Autoriza el borrador: debe cuadrar y no la autoriza quien la capturó; asigna el folio TT-000123. */
export function useAutorizarPoliza() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { poliza: Poliza; idempotencyKey: string }) =>
      (await apiRequest<Poliza>(`${BASE}/${a.poliza.id}/autorizar`, {
        method: 'POST', idempotencyKey: a.idempotencyKey, ifMatch: String(a.poliza.version),
      })).data,
    onSuccess: invalidar,
  });
}

/** Anula una autorizada: devuelve la NUEVA póliza de anulación (mismas partidas en negativo). */
export function useAnularPoliza() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { poliza: Poliza; body: AnularPolizaRequest; idempotencyKey: string }) =>
      (await apiRequest<Poliza>(`${BASE}/${a.poliza.id}/anular`, {
        method: 'POST', body: a.body, idempotencyKey: a.idempotencyKey, ifMatch: String(a.poliza.version),
      })).data,
    onSuccess: invalidar,
  });
}
