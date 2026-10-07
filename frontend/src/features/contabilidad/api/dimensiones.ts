import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type {
  Auxiliar,
  CentroCorporativo,
  CentroOpcion,
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
  TipoAuxiliar,
  TipoDocumento,
  UbicacionSucursal,
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
  ubicaciones: ['contabilidad', 'dimensiones', 'ubicaciones'] as const,
  corporativos: (q: string, solo: boolean) => ['contabilidad', 'dimensiones', 'corporativos', q, solo] as const,
  auxiliares: (tipo: TipoAuxiliar, q: string) => ['contabilidad', 'dimensiones', 'auxiliares', tipo, q] as const,
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

/** Borra una regla que todavía no ha validado movimientos (capturada por error). */
export function useEliminarRegla() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (regla: Regla) =>
      (await apiRequest<void>(`${BASE}/reglas-dimension/${regla.id}`, {
        method: 'DELETE',
        idempotencyKey: crypto.randomUUID(),
        ifMatch: String(regla.version),
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

/** Ubicaciones (Dim1) con su sucursal. */
export function useUbicacionesSucursal() {
  return useQuery({
    queryKey: dimKeys.ubicaciones,
    queryFn: async ({ signal }) => (await apiRequest<UbicacionSucursal[]>(`${BASE}/ubicaciones-sucursal`, { signal })).data,
  });
}

/** Liga una ubicación a una sucursal (null la desliga). */
export function useAsignarSucursalUbicacion() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { dim1Id: string; sucursalId: string | null }) =>
      (await apiRequest<UbicacionSucursal>(`${BASE}/ubicaciones-sucursal/${a.dim1Id}`, {
        method: 'PUT',
        body: { sucursalId: a.sucursalId },
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: invalidar,
  });
}

export function useCentrosCorporativos(q: string, soloCorporativos: boolean) {
  return useQuery({
    queryKey: dimKeys.corporativos(q, soloCorporativos),
    queryFn: async ({ signal }) =>
      (await apiRequest<CentroCorporativo[]>(
        `${BASE}/centros-corporativos${qs({ q, soloCorporativos: soloCorporativos || undefined })}`, { signal })).data,
  });
}

export function useMarcarCorporativo() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { dim2Id: string; corporativo: boolean }) =>
      (await apiRequest<CentroCorporativo>(`${BASE}/centros-corporativos/${a.dim2Id}`, {
        method: 'PUT',
        body: { corporativo: a.corporativo },
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: invalidar,
  });
}

/** Clientes, proveedores o cuentas bancarias activos para capturar una partida. */
export function useAuxiliares(tipo: TipoAuxiliar, q: string, enabled = true) {
  return useQuery({
    queryKey: dimKeys.auxiliares(tipo, q),
    enabled,
    queryFn: async ({ signal }) =>
      (await apiRequest<Auxiliar[]>(`${BASE}/movimientos/auxiliares${qs({ tipo, q })}`, { signal })).data,
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
