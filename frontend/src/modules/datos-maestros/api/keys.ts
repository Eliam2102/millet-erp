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
  EstadoEjecucionSync,
  EstatusCatalogo,
  Naturaleza,
  OrigenMaster,
  ResultadoSincronizacion,
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
  referenciaExterna?: string;
  resultadoSincronizacion?: ResultadoSincronizacion;
  offset?: number;
  limit?: number;
}

export interface ListarEjecucionesSyncFiltros {
  estado?: EstadoEjecucionSync;
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
  /** Tipo de A+W, igualdad exacta (Vidrio plano, VTE, VLA, VC). */
  tipo?: string;
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
  proveedorDatosBancarios: (id: string) =>
    [...datosMaestrosKeys.proveedor(id), 'datos-bancarios'] as const,

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

  clientesSync: () => [...datosMaestrosKeys.clientes(), 'sync'] as const,
  clientesSyncList: (filtros: ListarEjecucionesSyncFiltros) =>
    [...datosMaestrosKeys.clientesSync(), 'list', filtros] as const,
  clienteSync: (id: string) =>
    [...datosMaestrosKeys.clientesSync(), 'detail', id] as const,

  productosAw: () => [...datosMaestrosKeys.all, 'productos-aw'] as const,
  productosAwList: (filtros: ListarProductosAwFiltros) =>
    [...datosMaestrosKeys.productosAw(), 'list', filtros] as const,
  productoAw: (id: string) =>
    [...datosMaestrosKeys.productosAw(), 'detail', id] as const,
  productoAwSync: (id: string) =>
    [...datosMaestrosKeys.productosAw(), 'sync', id] as const,
} as const;
