import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type {
  CentroOpcion,
  CentroSucursales,
  Dimension,
  EditarReglaBody,
  FiltrosReglas,
  MovimientoBody,
  MovimientoPrueba,
  NuevaReglaBody,
  PaginaMovimientos,
  PaginaReglas,
  Regla,
  RequerimientoEfectivo,
  Sucursal,
  TipoDocumento,
  Validacion,
} from './dimensiones-types';

const BASE = '/api/v1/contabilidad';

export const dimKeys = {
  all: ['contabilidad', 'dimensiones'] as const,
  tipos: (incluirInactivos: boolean) => ['contabilidad', 'dimensiones', 'tipos', incluirInactivos] as const,
  reglas: (f: FiltrosReglas) => ['contabilidad', 'dimensiones', 'reglas', f] as const,
  matriz: (cuentaId: string, tipoDocumentoId: string, fecha: string) =>
    ['contabilidad', 'dimensiones', 'matriz', cuentaId, tipoDocumentoId, fecha] as const,
  sucursales: ['contabilidad', 'dimensiones', 'sucursales'] as const,
  sucursalesMovimiento: ['contabilidad', 'dimensiones', 'sucursales-movimiento'] as const,
  centrosSucursal: (q: string, sucursalId: string, soloSin: boolean) =>
    ['contabilidad', 'dimensiones', 'centros-sucursal', q, sucursalId, soloSin] as const,
  centros: (sucursalId: string, nivel: Dimension, dim2Id: string, q: string) =>
    ['contabilidad', 'dimensiones', 'centros', sucursalId, nivel, dim2Id, q] as const,
  movimientos: (offset: number) => ['contabilidad', 'dimensiones', 'movimientos', offset] as const,
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
  return () => void qc.invalidateQueries({ queryKey: dimKeys.all });
}

// ─── Tipos de documento ──────────────────────────────────────────────────────

export function useTiposDocumento(incluirInactivos = false) {
  return useQuery({
    queryKey: dimKeys.tipos(incluirInactivos),
    queryFn: async ({ signal }) =>
      (await apiRequest<TipoDocumento[]>(`${BASE}/tipos-documento${qs({ incluirInactivos: incluirInactivos || undefined })}`, { signal })).data,
  });
}

export function useCrearTipoDocumento() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (body: { clave: string; nombre: string; esPrueba: boolean }) =>
      (await apiRequest<TipoDocumento>(`${BASE}/tipos-documento`, { method: 'POST', body, idempotencyKey: crypto.randomUUID() })).data,
    onSuccess: invalidar,
  });
}

export function useEditarTipoDocumento() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (t: TipoDocumento & { nuevoNombre?: string; nuevoActivo?: boolean }) =>
      (await apiRequest<TipoDocumento>(`${BASE}/tipos-documento/${t.id}`, {
        method: 'PUT',
        body: { nombre: t.nuevoNombre ?? t.nombre, activo: t.nuevoActivo ?? t.activo },
        idempotencyKey: crypto.randomUUID(),
        ifMatch: String(t.version),
      })).data,
    onSuccess: invalidar,
  });
}

// ─── Reglas ──────────────────────────────────────────────────────────────────

export function useReglas(filtros: FiltrosReglas) {
  return useQuery({
    queryKey: dimKeys.reglas(filtros),
    queryFn: async ({ signal }) =>
      (await apiRequest<PaginaReglas>(`${BASE}/reglas-dimension${qs({ ...filtros })}`, { signal })).data,
  });
}

/** Matriz efectiva de una cuenta + tipo a una fecha. Sin cuenta no se consulta. */
export function useMatrizEfectiva(cuentaId: string, tipoDocumentoId: string, fecha: string) {
  return useQuery({
    queryKey: dimKeys.matriz(cuentaId, tipoDocumentoId, fecha),
    enabled: !!cuentaId,
    queryFn: async ({ signal }) =>
      (await apiRequest<RequerimientoEfectivo[]>(
        `${BASE}/reglas-dimension/efectivas${qs({ cuentaId, tipoDocumentoId, fecha })}`, { signal })).data,
  });
}

export function useCrearRegla() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (body: NuevaReglaBody) =>
      (await apiRequest<Regla>(`${BASE}/reglas-dimension`, { method: 'POST', body, idempotencyKey: crypto.randomUUID() })).data,
    onSuccess: invalidar,
  });
}

export function useEditarRegla() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { regla: Regla; body: EditarReglaBody }) =>
      (await apiRequest<Regla>(`${BASE}/reglas-dimension/${a.regla.id}`, {
        method: 'PUT',
        body: a.body,
        idempotencyKey: crypto.randomUUID(),
        ifMatch: String(a.regla.version),
      })).data,
    onSuccess: invalidar,
  });
}

export function useCerrarRegla() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { regla: Regla; vigenteHasta: string }) =>
      (await apiRequest<Regla>(`${BASE}/reglas-dimension/${a.regla.id}/cerrar`, {
        method: 'POST',
        body: { vigenteHasta: a.vigenteHasta },
        idempotencyKey: crypto.randomUUID(),
        ifMatch: String(a.regla.version),
      })).data,
    onSuccess: invalidar,
  });
}

// ─── Sucursales y centros ────────────────────────────────────────────────────

/** Todas las sucursales de la empresa (configuración de centros). */
export function useSucursalesContables() {
  return useQuery({
    queryKey: dimKeys.sucursales,
    queryFn: async ({ signal }) => (await apiRequest<Sucursal[]>(`${BASE}/sucursales`, { signal })).data,
  });
}

/** Sucursales que el usuario puede operar al capturar un movimiento (ADR-0051). */
export function useSucursalesMovimiento() {
  return useQuery({
    queryKey: dimKeys.sucursalesMovimiento,
    queryFn: async ({ signal }) => (await apiRequest<Sucursal[]>(`${BASE}/movimientos/sucursales`, { signal })).data,
  });
}

export function useCentrosSucursal(q: string, sucursalId: string, soloSinSucursal: boolean) {
  return useQuery({
    queryKey: dimKeys.centrosSucursal(q, sucursalId, soloSinSucursal),
    queryFn: async ({ signal }) =>
      (await apiRequest<CentroSucursales[]>(
        `${BASE}/centros-sucursal${qs({ q, sucursalId, soloSinSucursal: soloSinSucursal || undefined })}`, { signal })).data,
  });
}

export function useAsignarSucursales() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { dim2Id: string; sucursalIds: string[] }) =>
      (await apiRequest<CentroSucursales>(`${BASE}/centros-sucursal/${a.dim2Id}`, {
        method: 'PUT',
        body: { sucursalIds: a.sucursalIds },
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: invalidar,
  });
}

/** Centros activos seleccionables en la sucursal (los de otra sucursal no aparecen). Sin sucursal no se consulta. */
export function useCentrosParaMovimiento(sucursalId: string, nivel: Dimension, dim2Id: string, q: string, enabled = true) {
  return useQuery({
    queryKey: dimKeys.centros(sucursalId, nivel, dim2Id, q),
    enabled: enabled && !!sucursalId,
    queryFn: async ({ signal }) =>
      (await apiRequest<CentroOpcion[]>(`${BASE}/movimientos/centros${qs({ sucursalId, nivel, dim2Id, q })}`, { signal })).data,
  });
}

// ─── Movimientos ─────────────────────────────────────────────────────────────

/** Valida sin guardar. */
export function useValidarMovimientoDimensiones() {
  return useMutation({
    mutationFn: async (body: MovimientoBody) =>
      (await apiRequest<Validacion>(`${BASE}/movimientos/validar`, { method: 'POST', body })).data,
  });
}

/** Registra un movimiento de PRUEBA; 422 con `errores[]` si no cumple las reglas. */
export function useConfirmarMovimientoPrueba() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (body: MovimientoBody) =>
      (await apiRequest<MovimientoPrueba>(`${BASE}/movimientos-prueba`, { method: 'POST', body, idempotencyKey: crypto.randomUUID() })).data,
    onSuccess: invalidar,
  });
}

export function useMovimientosPrueba(offset: number, limit = 10) {
  return useQuery({
    queryKey: dimKeys.movimientos(offset),
    queryFn: async ({ signal }) =>
      (await apiRequest<PaginaMovimientos>(`${BASE}/movimientos-prueba${qs({ offset, limit })}`, { signal })).data,
  });
}
