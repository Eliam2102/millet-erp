import { useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * H1 · Área (Dim2) y Equipo (Dim3) en la captura de gastos de CxP (caja chica, viáticos y tarjeta
 * corporativa). La sucursal sale de la comprobación (caja chica) o del empleado (viáticos y tarjeta);
 * la API dice si la cuenta del gasto exige área/equipo (regla de dimensiones C-03).
 */

const BASE = '/api/v1/cuentas-por-pagar/gastos';

/** De dónde sale la sucursal del gasto: la de la comprobación o la del empleado. */
export type OrigenDimensionesGasto =
  | { sucursalId: string; empleadoId?: undefined }
  | { empleadoId: string; sucursalId?: undefined };

export interface DimensionesGasto {
  /** Sucursal resuelta; null si el empleado no tiene sucursal (el gasto no se puede dimensionar). */
  sucursalId: string | null;
  exigeArea: boolean;
  exigeEquipo: boolean;
  cuentaCodigo: string | null;
}

export type NivelCentroGasto = 2 | 3;

export interface CentroGastoOpcion {
  id: string;
  nivel: NivelCentroGasto;
  clave: string;
  nombre: string;
  dim2Id: string | null;
  dim2Clave: string | null;
}

export const dimensionesGastoKeys = {
  all: ['cxp', 'gastos', 'dimensiones'] as const,
  dimensiones: (sucursalId: string, empleadoId: string) =>
    [...dimensionesGastoKeys.all, 'reglas', { sucursalId, empleadoId }] as const,
  centros: (sucursalId: string, nivel: NivelCentroGasto, dim2Id: string, q: string) =>
    [...dimensionesGastoKeys.all, 'centros', { sucursalId, nivel, dim2Id, q }] as const,
};

function qs(params: Record<string, string | number | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== '') sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}

/** Qué exige la cuenta del gasto y en qué sucursal se buscan los centros. */
export function useDimensionesGasto(origen: OrigenDimensionesGasto) {
  const sucursalId = origen.sucursalId ?? '';
  const empleadoId = origen.empleadoId ?? '';
  return useQuery({
    queryKey: dimensionesGastoKeys.dimensiones(sucursalId, empleadoId),
    enabled: !!sucursalId || !!empleadoId,
    staleTime: 60_000,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<DimensionesGasto>(
          `${BASE}/dimensiones${qs(sucursalId ? { sucursalId } : { empleadoId })}`,
          { signal },
        )
      ).data,
  });
}

/** Áreas (nivel 2) o equipos (nivel 3, acotados al área con `dim2Id`) de la sucursal; búsqueda en servidor. */
export function useCentrosGasto(
  sucursalId: string,
  nivel: NivelCentroGasto,
  dim2Id: string,
  q: string,
  enabled = true,
) {
  return useQuery({
    queryKey: dimensionesGastoKeys.centros(sucursalId, nivel, dim2Id, q),
    enabled: enabled && !!sucursalId,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<CentroGastoOpcion[]>(
          `${BASE}/centros${qs({ sucursalId, nivel, dim2Id, q, limit: 50 })}`,
          { signal },
        )
      ).data,
  });
}

/**
 * Validación de C-03 en el submit: con `exigeArea` hace falta un centro; con `exigeEquipo`, que sea un
 * equipo (nivel 3). Un id precargado de nivel desconocido se deja pasar: la API vuelve a validar.
 */
export function validarCentroCostoGasto(
  dimensiones: DimensionesGasto | undefined,
  centroCostoId: string | null | undefined,
  nivel: NivelCentroGasto | undefined,
): string | null {
  if (!dimensiones?.sucursalId) return null;
  if ((dimensiones.exigeArea || dimensiones.exigeEquipo) && !centroCostoId)
    return dimensiones.exigeEquipo
      ? 'Captura el área y el equipo: la cuenta del gasto los exige (C-03).'
      : 'Captura el área: la cuenta del gasto la exige (C-03).';
  if (dimensiones.exigeEquipo && nivel === 2)
    return 'Captura el equipo: la cuenta del gasto lo exige (C-03).';
  return null;
}

/**
 * Recuerda el nivel (área/equipo) de cada id elegido para validar en el submit sin guardar el nivel en el
 * formulario.
 */
export function useNivelesCentroGasto() {
  const niveles = useRef(new Map<string, NivelCentroGasto>());
  return {
    registrar(id: string | null, nivel: NivelCentroGasto | null) {
      if (id && nivel) niveles.current.set(id, nivel);
    },
    nivel(id: string | null | undefined): NivelCentroGasto | undefined {
      return id ? niveles.current.get(id) : undefined;
    },
  };
}
