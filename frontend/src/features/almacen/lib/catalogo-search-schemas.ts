import { z } from 'zod';
import {
  EstatusCatalogo,
  NivelReorden,
  TipoSubAlmacen,
} from '@/features/almacen/api/types';

/**
 * Schemas Zod para los <c>search</c> params de las bandejas del
 * catálogo Almacén / Sub-Almacén (F1-PR1). TanStack Router los usa
 * en <c>validateSearch</c> para filtrar valores inválidos y aplicar
 * defaults.
 */

export const AlmacenesSearchSchema = z.object({
  q: z.string().min(1).max(200).optional(),
  estatus: z
    .union([
      z.literal(EstatusCatalogo.Activo),
      z.literal(EstatusCatalogo.Inactivo),
      z.literal(EstatusCatalogo.Borrador),
    ])
    .optional(),
  sucursalId: z.string().min(1).optional(),
});

export type AlmacenesSearch = z.infer<typeof AlmacenesSearchSchema>;

export const DEFAULT_ALMACENES_SEARCH: AlmacenesSearch = {};

export const SubAlmacenesSearchSchema = z.object({
  q: z.string().min(1).max(200).optional(),
  almacenId: z.string().min(1).optional(),
  tipo: z
    .union([
      z.literal(TipoSubAlmacen.Insumos),
      z.literal(TipoSubAlmacen.MaterialesDirectos),
      z.literal(TipoSubAlmacen.MaterialEnRevision),
      z.literal(TipoSubAlmacen.Transitorio),
    ])
    .optional(),
  estatus: z
    .union([
      z.literal(EstatusCatalogo.Activo),
      z.literal(EstatusCatalogo.Inactivo),
      z.literal(EstatusCatalogo.Borrador),
    ])
    .optional(),
});

export type SubAlmacenesSearch = z.infer<typeof SubAlmacenesSearchSchema>;

export const DEFAULT_SUB_ALMACENES_SEARCH: SubAlmacenesSearch = {};

/**
 * Search params de la bandeja de reabasto (código = reorden, ADR-0047 PR5.F).
 * Filtros: artículo, nivel (N1/N2) y estatus. La entidad NO se filtra en la
 * barra (es polimórfica según el nivel; los tres cubren los cortes reales).
 */
export const ReordenSearchSchema = z.object({
  articuloId: z.string().min(1).optional(),
  nivel: z
    .union([z.literal(NivelReorden.Sucursal), z.literal(NivelReorden.Almacen)])
    .optional(),
  estatus: z
    .union([
      z.literal(EstatusCatalogo.Activo),
      z.literal(EstatusCatalogo.Inactivo),
      z.literal(EstatusCatalogo.Borrador),
    ])
    .optional(),
});

export type ReordenSearch = z.infer<typeof ReordenSearchSchema>;

export const DEFAULT_REORDEN_SEARCH: ReordenSearch = {};

/**
 * Search params de "Ubicación de artículos" (asignación N4, ADR-0047 PR C).
 * Filtros: artículo y estatus. La ubicación no se filtra en la barra (se ve en
 * la columna, resuelta con su padre).
 */
export const AsignacionesSearchSchema = z.object({
  articuloId: z.string().min(1).optional(),
  estatus: z
    .union([
      z.literal(EstatusCatalogo.Activo),
      z.literal(EstatusCatalogo.Inactivo),
      z.literal(EstatusCatalogo.Borrador),
    ])
    .optional(),
  // Atajo con retorno: sentinel que marca "llegué por un atajo desde un
  // artículo" (vs filtré manualmente). Dispara el breadcrumb de "Volver".
  // `articuloEtiqueta` (clave · nombre) la trae el origen para pintar el label
  // sin fetch; opcional → deep-link manual cae a un breadcrumb genérico.
  desde: z.literal('articulo').optional(),
  articuloEtiqueta: z.string().optional(),
});

export type AsignacionesSearch = z.infer<typeof AsignacionesSearchSchema>;

export const DEFAULT_ASIGNACIONES_SEARCH: AsignacionesSearch = {};

/**
 * Search params de la gestión de ubicaciones N4 (racks/pasillos, ADR-0047 PR
 * C7.1). Filtros: sub-almacén y estatus. La búsqueda libre se difiere (el
 * catálogo por sub-almacén es chico).
 */
export const UbicacionesSearchSchema = z.object({
  subAlmacenId: z.string().min(1).optional(),
  estatus: z
    .union([
      z.literal(EstatusCatalogo.Activo),
      z.literal(EstatusCatalogo.Inactivo),
      z.literal(EstatusCatalogo.Borrador),
    ])
    .optional(),
});

export type UbicacionesSearch = z.infer<typeof UbicacionesSearchSchema>;

export const DEFAULT_UBICACIONES_SEARCH: UbicacionesSearch = {};
