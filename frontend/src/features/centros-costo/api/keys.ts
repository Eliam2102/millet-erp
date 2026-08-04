import type { NodoTipoJerarquia } from './types';

/**
 * Query keys de TanStack Query del módulo Centros de Costo. Mismo shape
 * que <c>almacenKeys</c>: <c>['centros-costo', recurso, ...filtros]</c>;
 * el namespace permite invalidación masiva del módulo.
 */
export interface HijosJerarquiaCeCoParams {
  nodoTipo: NodoTipoJerarquia;
  nodoId?: string;
  incluirInactivos?: boolean;
}

export const centrosCostoKeys = {
  all: ['centros-costo'] as const,
  jerarquia: (params: HijosJerarquiaCeCoParams) =>
    [
      'centros-costo',
      'jerarquia',
      params.nodoTipo,
      params.nodoId ?? null,
      params.incluirInactivos ?? false,
    ] as const,
  jerarquiaAll: ['centros-costo', 'jerarquia'] as const,
  // Detalle por recurso: el GET captura el ETag para el If-Match de las
  // mutaciones (molde useMutacionConIfMatch de Cajas).
  detalle: (recurso: RecursoCatalogo, id: string) =>
    ['centros-costo', recurso, 'detalle', id] as const,
  lista: (recurso: RecursoCatalogo, filtros: object) =>
    ['centros-costo', recurso, 'lista', filtros] as const,
  // Árbol de asignación por usuario (FE-PR3).
  arbolAsignacion: (usuarioId: string) =>
    ['centros-costo', 'asignacion', 'arbol', usuarioId] as const,
  // Búsqueda del selector "Máquina" (Fase E). Se llavea por `endpoint`
  // porque el mismo picker sirve al selector filtrado y a los abiertos por
  // proxy (rutas distintas) — el endpoint es lo que decide el conjunto.
  buscarDim3: (endpoint: string, filtros: object) =>
    ['centros-costo', 'dim3', 'buscar', endpoint, filtros] as const,
};

/** Segmento de ruta del recurso bajo /api/v1/centros-costo/. */
export type RecursoCatalogo =
  | 'dim1'
  | 'dim2'
  | 'dim3'
  | 'grupos-dim2'
  | 'grupos-dim3';
