import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** Matriz de documentos del expediente de proveedor (tipos configurables). */
const RUTA = '/api/v1/datos-maestros/proveedores/expediente/tipos';
const KEY = ['datos-maestros', 'proveedores', 'expediente-tipos'] as const;

export interface TipoExpedienteProveedor {
  id: string;
  codigo: string;
  nombre: string;
  orden: number;
  obligatorio: boolean;
  vigenciaMeses: number | null;
  soloPersonaMoral: boolean;
  soloNacional: boolean;
  activo: boolean;
  version: number;
}

export interface TipoExpedienteProveedorPayload {
  codigo: string;
  nombre: string;
  obligatorio: boolean;
  vigenciaMeses: number | null;
  soloPersonaMoral: boolean;
  soloNacional: boolean;
  activo: boolean;
}

export function useTiposExpedienteProveedor() {
  return useQuery({
    queryKey: KEY,
    queryFn: async ({ signal }) =>
      (await apiRequest<TipoExpedienteProveedor[]>(RUTA, { signal })).data,
  });
}

export function useGuardarTipoExpedienteProveedor() {
  const qc = useQueryClient();
  return useMutation<
    unknown,
    Error,
    { tipoId?: string; version?: number; payload: TipoExpedienteProveedorPayload; idempotencyKey: string }
  >({
    mutationFn: async ({ tipoId, version, payload, idempotencyKey }) =>
      tipoId
        ? apiRequest<void>(`${RUTA}/${tipoId}`, {
            method: 'PUT',
            body: payload,
            idempotencyKey,
            headers: { 'X-Expected-Version': String(version) },
          })
        : apiRequest<{ id: string }>(RUTA, { method: 'POST', body: payload, idempotencyKey }),
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  });
}
