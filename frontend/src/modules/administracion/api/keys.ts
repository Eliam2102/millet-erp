/**
 * Query keys de TanStack Query para el módulo Administración. Mismo
 * shape que <c>comprasKeys</c>: <c>['admin', recurso, acción, ...]</c>.
 *
 * <para>Convención cross-módulo: el primer slot es el módulo (para
 * invalidación masiva al cambiar de empresa/usuario), el segundo es
 * el recurso. Replicar al traer más sub-recursos.</para>
 */

import type { TipoDocumentoSerie } from '@/modules/administracion/api/types';

export interface ListarEmpresasFiltros {
  soloActivas?: boolean;
  offset?: number;
  limit?: number;
}

/**
 * Filtros del listado de Series (F-Admin-PR6.1). Espejo del query
 * string aceptado por <c>GET /api/v1/admin/series</c>.
 */
export interface ListarSeriesFiltros {
  empresaId?: string | null;
  tipoDocumento?: TipoDocumentoSerie | null;
  offset?: number;
  limit?: number;
}

/**
 * Filtros del consolidado de auditoría (F-Admin-PR7.2). Espejo del
 * query string de <c>GET /api/v1/admin/auditoria</c>.
 *
 * <para><c>desde</c> y <c>hasta</c> son obligatorios (formato
 * <c>YYYY-MM-DD</c>); el endpoint rechaza con 400 si faltan. El hook
 * los enforce vía <c>enabled</c> antes de disparar la query.</para>
 */
export interface ConsultarBitacoraFiltros {
  desde: string;
  hasta: string;
  modulo?: string;
  recurso?: string;
  accion?: string;
  usuarioId?: string;
  empresaId?: string;
  offset?: number;
  limit?: number;
}

export const adminKeys = {
  all: ['admin'] as const,

  empresas: () => [...adminKeys.all, 'empresas'] as const,
  empresasList: (filtros: ListarEmpresasFiltros) =>
    [...adminKeys.empresas(), 'list', filtros] as const,
  empresa: (id: string) => [...adminKeys.empresas(), 'detail', id] as const,

  series: () => [...adminKeys.all, 'series'] as const,
  seriesList: (filtros: ListarSeriesFiltros) =>
    [...adminKeys.series(), 'list', filtros] as const,
  serie: (id: string) => [...adminKeys.series(), 'detail', id] as const,

  auditoria: () => [...adminKeys.all, 'auditoria'] as const,
  auditoriaList: (filtros: ConsultarBitacoraFiltros) =>
    [...adminKeys.auditoria(), 'list', filtros] as const,

  parametros: () => [...adminKeys.all, 'parametros'] as const,
  parametrosList: (modulo?: string | null) =>
    [...adminKeys.parametros(), 'list', modulo ?? null] as const,
  parametro: (clave: string) =>
    [...adminKeys.parametros(), 'detail', clave] as const,

  // FAC-ING-PR3: catálogo administrable de canales de venta
  // (compartido.canales_venta). Lista corta sin paginación; el filtro
  // por estatus vive en la key para cachear cada vista.
  canalesVenta: () => [...adminKeys.all, 'canales-venta'] as const,
  canalesVentaList: (estatus?: number | null) =>
    [...adminKeys.canalesVenta(), 'list', estatus ?? null] as const,

  // ADM-FE-PR1 (doc 10-catalogo-puestos-empleados): masters de puestos
  // y empleados. La lista se lee de GET /catalogos/{puestos,empleados}
  // (incluye inactivos para el admin); las mutaciones van a /admin/*.
  puestos: () => [...adminKeys.all, 'puestos'] as const,
  puestosList: () => [...adminKeys.puestos(), 'list'] as const,
  empleados: () => [...adminKeys.all, 'empleados'] as const,
  empleadosList: () => [...adminKeys.empleados(), 'list'] as const,

  // PR-A3: asignaciones N:M Sucursal ↔ Departamento. Scopeadas por
  // sucursal porque el endpoint es <c>GET /admin/empresas/sucursales/{id}/departamentos</c>.
  // Cualquier mutación (asignar/desactivar/reactivar) invalida la lista
  // de su sucursal.
  sucursalDepartamentos: () =>
    [...adminKeys.all, 'sucursal-departamentos'] as const,
  sucursalDepartamentosList: (sucursalId: string) =>
    [...adminKeys.sucursalDepartamentos(), 'list', sucursalId] as const,
} as const;
