/**
 * DTOs de los catálogos organizacionales (mirror de
 * <c>OrganizacionEndpoints.cs</c> y <c>UsuariosEndpoints.cs</c>) que
 * el módulo Compras consume para resolver IDs → nombres en bandejas y
 * detalles. La capa <c>features/catalogos/</c> es transversal: cuando
 * lleguen CxC, OC, etc., la consumirán igual.
 */

import type {
  EstatusCatalogo,
  Naturaleza,
} from '@/features/compras/api/types';

/**
 * Tipo de persona del proveedor (mirror de
 * <c>TipoPersonaProveedor.cs</c> en SharedKernel.Domain). Régimen
 * fiscal MX.
 */
export const TipoPersonaProveedor = {
  Moral: 0,
  Fisica: 1,
} as const satisfies Record<string, number>;
export type TipoPersonaProveedor =
  (typeof TipoPersonaProveedor)[keyof typeof TipoPersonaProveedor];

/** Mirror de <c>SucursalListItem</c>. */
export interface SucursalListItem {
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
}

/** Mirror de <c>DepartamentoListItem</c>. */
export interface DepartamentoListItem {
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
}

/** Mirror de <c>PuestoListItem</c> (ADM-FE-PR1). */
export interface PuestoListItem {
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
  rolSugeridoId?: string | null;
  rolSugeridoNombre?: string | null;
  departamentoId?: string | null;
  departamentoNombre?: string | null;
}

/**
 * Mirror de <c>EmpleadoListItem</c> (ADM-FE-PR1). Trae <c>puestoId</c>
 * y <c>jefeDirectoId</c> desnormalizados para que la solicitud de
 * viáticos prellene tope (política por puesto) y autorizador N1 sin
 * fetch extra.
 */
export interface EmpleadoListItem {
  id: string;
  empresaId: string;
  clave: string;
  nombre: string;
  email: string | null;
  puestoId: string | null;
  jefeDirectoId: string | null;
  sucursalId: string | null;
  departamentoId: string | null;
  usuarioId: string | null;
  estatus: EstatusCatalogo;
  puestoNombre?: string | null;
  departamentoNombre?: string | null;
}

/** Mirror de <c>AlmacenListItem</c>. */
export interface AlmacenListItem {
  id: string;
  clave: string;
  nombre: string;
  sucursalId: string;
  estatus: EstatusCatalogo;
}

/** Mirror de <c>UsuarioListItem</c>. <b>NO</b> expone <c>entraOid</c>. */
export interface UsuarioListItem {
  id: string;
  email: string;
  nombre: string;
  departamentoId: string | null;
  activo: boolean;
}

/**
 * Mirror de <c>ArticuloListItem</c>. <c>claveLegacy</c>,
 * <c>descripcionLarga</c> y <c>precioReferencia</c> solo aparecen en
 * el detalle (no en la lista) para mantener el payload chico.
 */
export interface ArticuloListItem {
  id: string;
  clave: string;
  nombre: string;
  unidadMedidaDefault: string;
  /** FK al catálogo de unidades (ADR-0046 1b). El JSON ya lo trae; null en
   *  artículos legacy sin reconciliar → la validación de decimales cae al
   *  fallback (Etapa 2 PR-2c). */
  unidadMedidaId: string | null;
  naturaleza: Naturaleza;
  categoria: string | null;
  estatus: EstatusCatalogo;
}

/**
 * Detalle por id (<c>GET /api/v1/catalogos/articulos/{id}</c>) — cabecera
 * completa; incluye campos que el list no expone (claveLegacy,
 * descripcionLarga, precioReferencia).
 */
export interface ArticuloDetalle {
  id: string;
  clave: string;
  claveLegacy: string | null;
  nombre: string;
  descripcionLarga: string | null;
  unidadMedidaDefault: string;
  unidadMedidaId: string | null;
  naturaleza: Naturaleza;
  categoria: string | null;
  categoriaId: string | null;
  precioReferenciaMonto: number | null;
  precioReferenciaMoneda: string | null;
  estatus: EstatusCatalogo;
}

/** Mirror de <c>ProveedorListItem</c>. */
export interface ProveedorListItem {
  id: string;
  clave: string;
  razonSocial: string;
  nombreComercial: string | null;
  rfc: string;
  tipoPersona: TipoPersonaProveedor;
  condicionesPagoDias: number | null;
  monedaPreferidaId: string | null;
  estatus: EstatusCatalogo;
}

/**
 * Mirror de <c>ProveedorDetalle</c> — cabecera completa de
 * <c>GET /api/v1/catalogos/proveedores/{id}</c> (incluye claveLegacy, email y
 * teléfono que el list no expone). La consume <c>useProveedor(id)</c> para
 * resolver la etiqueta de un proveedor cuyo id viene "frío" (sin pasar por el
 * typeahead); reutilizable por cualquier selector/vista que tenga el id.
 */
export interface ProveedorDetalle {
  id: string;
  clave: string;
  claveLegacy: string | null;
  razonSocial: string;
  nombreComercial: string | null;
  rfc: string;
  tipoPersona: TipoPersonaProveedor;
  condicionesPagoDias: number | null;
  monedaPreferidaId: string | null;
  email: string | null;
  telefono: string | null;
  estatus: EstatusCatalogo;
}

/**
 * Forma del response paginado del backend para los catálogos
 * (<c>PagedCatalogoResponse&lt;T&gt;</c>). Mismo shape que
 * <c>PagedResponse&lt;T&gt;</c> de Compras pero declarado por
 * separado para no acoplar features.
 */
export interface PagedCatalogoResponse<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}
