/**
 * Query keys de TanStack Query para el módulo Datos Maestros. Mismo
 * shape que <c>adminKeys</c> y <c>identidadKeys</c>:
 * <c>['datos-maestros', recurso, acción, ...]</c>.
 *
 * <para>Convención cross-módulo: el primer slot es el módulo (para
 * invalidación masiva), el segundo es el recurso (proveedores /
 * articulos), seguido de <c>list</c> + filtros o <c>detail</c> + id.
 * Los filtros se incluyen como objeto en la key para que cambios de
 * filtro disparen una nueva query (server-side filtering).</para>
 */

import type {
  EstatusCatalogo,
  Naturaleza,
  OrigenMaster,
  TipoPersonaProveedor,
} from '@/modules/datos-maestros/api/types';

export interface ListarProveedoresFiltros {
  rfc?: string;
  razonSocial?: string;
  tipoPersona?: TipoPersonaProveedor;
  estatus?: EstatusCatalogo;
  offset?: number;
  limit?: number;
}

export interface ListarArticulosFiltros {
  codigo?: string;
  descripcion?: string;
  naturaleza?: Naturaleza;
  unidadMedidaDefault?: string;
  estatus?: EstatusCatalogo;
  offset?: number;
  limit?: number;
}

export interface ListarClientesFiltros {
  rfc?: string;
  razonSocial?: string;
  origen?: OrigenMaster;
  estatus?: EstatusCatalogo;
  /** true = bandeja de trabajo pre-timbrado (falta RFC/régimen/CP). */
  fiscalesIncompletos?: boolean;
  offset?: number;
  limit?: number;
}

export interface ListarProductosAwFiltros {
  referencia?: string;
  descripcion?: string;
  origen?: OrigenMaster;
  estatus?: EstatusCatalogo;
  /** true = bandeja de trabajo pre-timbrado (faltan claves SAT). */
  fiscalesIncompletos?: boolean;
  offset?: number;
  limit?: number;
}

export const datosMaestrosKeys = {
  all: ['datos-maestros'] as const,

  proveedores: () => [...datosMaestrosKeys.all, 'proveedores'] as const,
  proveedoresList: (filtros: ListarProveedoresFiltros) =>
    [...datosMaestrosKeys.proveedores(), 'list', filtros] as const,
  proveedor: (id: string) =>
    [...datosMaestrosKeys.proveedores(), 'detail', id] as const,

  articulos: () => [...datosMaestrosKeys.all, 'articulos'] as const,
  articulosList: (filtros: ListarArticulosFiltros) =>
    [...datosMaestrosKeys.articulos(), 'list', filtros] as const,
  articulo: (id: string) =>
    [...datosMaestrosKeys.articulos(), 'detail', id] as const,

  clientes: () => [...datosMaestrosKeys.all, 'clientes'] as const,
  clientesList: (filtros: ListarClientesFiltros) =>
    [...datosMaestrosKeys.clientes(), 'list', filtros] as const,
  cliente: (id: string) =>
    [...datosMaestrosKeys.clientes(), 'detail', id] as const,

  productosAw: () => [...datosMaestrosKeys.all, 'productos-aw'] as const,
  productosAwList: (filtros: ListarProductosAwFiltros) =>
    [...datosMaestrosKeys.productosAw(), 'list', filtros] as const,
  productoAw: (id: string) =>
    [...datosMaestrosKeys.productosAw(), 'detail', id] as const,
} as const;
