import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { polizaKeys } from './polizas';
import type {
  CambiarCuentaRolRequest,
  CambioCuentaRol,
  CrearRolRequest,
  EstadoRechazo,
  PlantillaContable,
  RechazoContable,
  ResultadoReproceso,
  RolCuenta,
} from './motor-types';

/** Motor de pólizas automáticas (C1.4). Mutaciones con Idempotency-Key; el cambio de cuenta de un rol, con If-Match. */
const BASE = '/api/v1/contabilidad/motor';

export const motorKeys = {
  all: ['contabilidad', 'motor'] as const,
  roles: ['contabilidad', 'motor', 'roles'] as const,
  bitacora: (rolId: string) => ['contabilidad', 'motor', 'roles', rolId, 'bitacora'] as const,
  plantillas: ['contabilidad', 'motor', 'plantillas'] as const,
  rechazos: (estado: EstadoRechazo | undefined) => ['contabilidad', 'motor', 'rechazos', estado ?? 'todos'] as const,
  rechazosTodos: ['contabilidad', 'motor', 'rechazos'] as const,
};

export function useRolesCuenta() {
  return useQuery({
    queryKey: motorKeys.roles,
    queryFn: async ({ signal }) => (await apiRequest<RolCuenta[]>(`${BASE}/roles`, { signal })).data,
  });
}

export function useBitacoraRol(rolId: string | null) {
  return useQuery({
    queryKey: motorKeys.bitacora(rolId ?? 'none'),
    enabled: !!rolId,
    queryFn: async ({ signal }) => (await apiRequest<CambioCuentaRol[]>(`${BASE}/roles/${rolId}/bitacora`, { signal })).data,
  });
}

export function useCrearRol() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { body: CrearRolRequest; idempotencyKey: string }) =>
      (await apiRequest<RolCuenta>(`${BASE}/roles`, { method: 'POST', body: a.body, idempotencyKey: a.idempotencyKey })).data,
    onSuccess: () => void qc.invalidateQueries({ queryKey: motorKeys.roles }),
  });
}

/** Cambia la cuenta del rol (queda en su bitácora). 409 si otra persona lo cambió antes; 422 si la cuenta no es válida. */
export function useCambiarCuentaRol() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { rol: Pick<RolCuenta, 'id' | 'version'>; body: CambiarCuentaRolRequest; idempotencyKey: string }) =>
      (await apiRequest<RolCuenta>(`${BASE}/roles/${a.rol.id}/cuenta`, {
        method: 'PUT', body: a.body, idempotencyKey: a.idempotencyKey, ifMatch: String(a.rol.version),
      })).data,
    onSuccess: (_r, a) => {
      void qc.invalidateQueries({ queryKey: motorKeys.roles });
      void qc.invalidateQueries({ queryKey: motorKeys.bitacora(a.rol.id) });
    },
  });
}

export function usePlantillasContables(enabled = true) {
  return useQuery({
    queryKey: motorKeys.plantillas,
    enabled,
    queryFn: async ({ signal }) => (await apiRequest<PlantillaContable[]>(`${BASE}/plantillas`, { signal })).data,
  });
}

export function useRechazosContables(estado?: EstadoRechazo) {
  return useQuery({
    queryKey: motorKeys.rechazos(estado),
    queryFn: async ({ signal }) =>
      (await apiRequest<RechazoContable[]>(`${BASE}/rechazos${estado ? `?estado=${estado}` : ''}`, { signal })).data,
  });
}

/** Reprocesar puede generar la póliza: se invalidan también la bandeja y los detalles de pólizas. */
export function useReprocesarRechazo() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { id: string; idempotencyKey: string }) =>
      (await apiRequest<ResultadoReproceso>(`${BASE}/rechazos/${a.id}/reprocesar`, {
        method: 'POST', idempotencyKey: a.idempotencyKey,
      })).data,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: motorKeys.rechazosTodos });
      void qc.invalidateQueries({ queryKey: polizaKeys.all });
    },
  });
}

export function useDescartarRechazo() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { id: string; motivo: string; idempotencyKey: string }) =>
      (await apiRequest<RechazoContable>(`${BASE}/rechazos/${a.id}/descartar`, {
        method: 'POST', body: { motivo: a.motivo }, idempotencyKey: a.idempotencyKey,
      })).data,
    onSuccess: () => void qc.invalidateQueries({ queryKey: motorKeys.rechazosTodos }),
  });
}
