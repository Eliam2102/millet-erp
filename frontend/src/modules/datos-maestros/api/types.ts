/**
 * Tipos del API REST del módulo Datos Maestros — espejo de los DTOs
 * del backend en <c>Millet.DatosMaestros.Application.*</c> y
 * <c>Millet.Api.Endpoints.DatosMaestros.DatosMaestrosEndpoints</c>
 * (lectura enriquecida, F-Admin-PR4.5) +
 * <c>Millet.Api.Endpoints.Catalogos.CatalogosEndpoints</c> (mutaciones,
 * B.5).
 *
 * <para>Convención <b>limpiar*</b> en los PATCH: el backend distingue
 * "no se mandó" (campo ausente) de "explícitamente vacío" (flag
 * <c>limpiarX = true</c>). Mismo patrón que Empresa.</para>
 */

/**
 * Estatus de un catálogo cross-empresa. Mirror del enum
 * <c>EstatusCatalogo</c> backend (<c>short</c>).
 */
export const EstatusCatalogo = {
  Activo: 0,
  Inactivo: 1,
  EnRevision: 2,
} as const;
export type EstatusCatalogo =
  (typeof EstatusCatalogo)[keyof typeof EstatusCatalogo];

/**
 * Tipo persona para proveedores fiscales mexicanos. Mirror del enum
 * <c>TipoPersonaProveedor</c> backend.
 */
export const TipoPersonaProveedor = {
  Moral: 0,
  Fisica: 1,
} as const;
export type TipoPersonaProveedor =
  (typeof TipoPersonaProveedor)[keyof typeof TipoPersonaProveedor];

/**
 * Origen de un registro de master auto-provisionable (ADR-0048).
 * Mirror del enum <c>OrigenMaster</c> backend (<c>short</c>):
 * <c>Manual</c> = alta de operador; <c>Aw</c> = auto-provisionado por
 * la ingesta de pedidos A+W.
 */
export const OrigenMaster = {
  Manual: 0,
  Aw: 1,
} as const;
export type OrigenMaster = (typeof OrigenMaster)[keyof typeof OrigenMaster];

/**
 * Naturaleza del artículo — alimenta la matriz de aprobación
 * A1 §3.bis del módulo Compras. Mirror del enum <c>Naturaleza</c>
 * backend.
 */
export const Naturaleza = {
  Estandar: 0,
  Servicio: 1,
  Critico: 2,
  Riesgo: 3,
} as const;
export type Naturaleza = (typeof Naturaleza)[keyof typeof Naturaleza];

// ─── Proveedores ───────────────────────────────────────────────────

export interface ProveedorItem {
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

export interface ListarProveedoresResponse {
  items: ProveedorItem[];
  offset: number;
  limit: number;
  total: number;
}

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

export interface CrearProveedorPayload {
  clave: string;
  razonSocial: string;
  rfc: string;
  tipoPersona: TipoPersonaProveedor;
  nombreComercial: string | null;
  condicionesPagoDias: number | null;
  monedaPreferidaId: string | null;
  email: string | null;
  telefono: string | null;
}

export interface ActualizarProveedorPayload {
  razonSocial?: string | null;
  nombreComercial?: string | null;
  rfc?: string | null;
  tipoPersona?: TipoPersonaProveedor | null;
  condicionesPagoDias?: number | null;
  monedaPreferidaId?: string | null;
  email?: string | null;
  telefono?: string | null;
  limpiarNombreComercial?: boolean | null;
  limpiarCondicionesPago?: boolean | null;
  limpiarMonedaPreferida?: boolean | null;
  limpiarEmail?: boolean | null;
  limpiarTelefono?: boolean | null;
}

export interface CrearProveedorResponse {
  id: string;
  clave: string;
}

// ─── Artículos ─────────────────────────────────────────────────────

export interface ArticuloItem {
  id: string;
  clave: string;
  nombre: string;
  unidadMedidaDefault: string;
  /** FK al catálogo de unidades (ADR-0046 Etapa 1b); null = legacy. */
  unidadMedidaId: string | null;
  naturaleza: Naturaleza;
  /** Nombre de la categoría (legacy o sincronizado del catálogo); display. */
  categoria: string | null;
  /** FK al catálogo de categorías (ADR-0046 PR2); null = no reconciliado. */
  categoriaId: string | null;
  estatus: EstatusCatalogo;
}

export interface ListarArticulosResponse {
  items: ArticuloItem[];
  offset: number;
  limit: number;
  total: number;
}

export interface ArticuloDetalle {
  id: string;
  clave: string;
  claveLegacy: string | null;
  nombre: string;
  descripcionLarga: string | null;
  unidadMedidaDefault: string;
  /** FK al catálogo de unidades (ADR-0046 Etapa 1b); null = legacy. */
  unidadMedidaId: string | null;
  naturaleza: Naturaleza;
  /** Nombre de la categoría (legacy o sincronizado del catálogo); display. */
  categoria: string | null;
  /** FK al catálogo de categorías (ADR-0046 PR2); null = no reconciliado. */
  categoriaId: string | null;
  precioReferenciaMonto: number | null;
  precioReferenciaMoneda: string | null;
  estatus: EstatusCatalogo;
}

export interface CrearArticuloPayload {
  clave: string;
  nombre: string;
  /** FK a una unidad activa del catálogo (ADR-0046 Etapa 1b). */
  unidadMedidaId: string;
  naturaleza: Naturaleza;
  descripcionLarga: string | null;
  /** FK a una categoría activa del catálogo (ADR-0046 PR2); null = sin categoría. */
  categoriaId: string | null;
  precioReferenciaMonto: number | null;
  precioReferenciaMoneda: string | null;
}

export interface ActualizarArticuloPayload {
  nombre?: string | null;
  descripcionLarga?: string | null;
  /** Si llega, reasigna la unidad (FK) y sincroniza el default (ADR-0046 1b). */
  unidadMedidaId?: string | null;
  naturaleza?: Naturaleza | null;
  /** Si llega, reasigna la categoría (FK) y sincroniza el nombre legacy. */
  categoriaId?: string | null;
  precioReferenciaMonto?: number | null;
  precioReferenciaMoneda?: string | null;
  limpiarDescripcionLarga?: boolean | null;
  limpiarCategoria?: boolean | null;
  limpiarPrecioReferencia?: boolean | null;
}

export interface CrearArticuloResponse {
  id: string;
  clave: string;
}

// ─── Clientes (ADR-0048) ───────────────────────────────────────────

export interface ClienteItem {
  id: string;
  clave: string;
  /** Correlación con A+W (código de cliente en A+W); inmutable. */
  referenciaExterna: string | null;
  razonSocial: string;
  rfc: string | null;
  regimenFiscal: string | null;
  codigoPostalFiscal: string | null;
  monedaDefault: string;
  esGenerico: boolean;
  origen: OrigenMaster;
  /** false = falta RFC, régimen fiscal o CP → no puede timbrar. */
  datosFiscalesCompletos: boolean;
  estatus: EstatusCatalogo;
}

export interface ListarClientesResponse {
  items: ClienteItem[];
  offset: number;
  limit: number;
  total: number;
}

export interface ClienteDetalle {
  id: string;
  clave: string;
  referenciaExterna: string | null;
  razonSocial: string;
  rfc: string | null;
  regimenFiscal: string | null;
  codigoPostalFiscal: string | null;
  usoCfdiDefault: string | null;
  formaPagoDefault: string | null;
  metodoPagoDefault: string | null;
  monedaDefault: string;
  esGenerico: boolean;
  origen: OrigenMaster;
  email: string | null;
  telefono: string | null;
  /** Receptor extranjero (CCE): tax id, país residencia, domicilio extranjero. */
  numRegIdTrib: string | null;
  paisResidencia: string | null;
  domicilioExtranjeroCalle: string | null;
  domicilioExtranjeroEstado: string | null;
  domicilioExtranjeroCodigoPostal: string | null;
  datosFiscalesCompletos: boolean;
  estatus: EstatusCatalogo;
}

export interface CrearClientePayload {
  clave: string;
  razonSocial: string;
  referenciaExterna: string | null;
  rfc: string | null;
  regimenFiscal: string | null;
  codigoPostalFiscal: string | null;
  usoCfdiDefault: string | null;
  formaPagoDefault: string | null;
  metodoPagoDefault: string | null;
  /** null = el backend aplica el default MXN. */
  monedaDefault: string | null;
  /** null = el backend aplica el default false. */
  esGenerico: boolean | null;
  email: string | null;
  telefono: string | null;
  numRegIdTrib?: string | null;
  paisResidencia?: string | null;
  domicilioExtranjeroCalle?: string | null;
  domicilioExtranjeroEstado?: string | null;
  domicilioExtranjeroCodigoPostal?: string | null;
}

export interface ActualizarClientePayload {
  razonSocial?: string | null;
  rfc?: string | null;
  regimenFiscal?: string | null;
  codigoPostalFiscal?: string | null;
  usoCfdiDefault?: string | null;
  formaPagoDefault?: string | null;
  metodoPagoDefault?: string | null;
  monedaDefault?: string | null;
  esGenerico?: boolean | null;
  email?: string | null;
  telefono?: string | null;
  numRegIdTrib?: string | null;
  paisResidencia?: string | null;
  domicilioExtranjeroCalle?: string | null;
  domicilioExtranjeroEstado?: string | null;
  domicilioExtranjeroCodigoPostal?: string | null;
  limpiarRfc?: boolean | null;
  limpiarRegimenFiscal?: boolean | null;
  limpiarCodigoPostalFiscal?: boolean | null;
  limpiarUsoCfdiDefault?: boolean | null;
  limpiarFormaPagoDefault?: boolean | null;
  limpiarMetodoPagoDefault?: boolean | null;
  limpiarEmail?: boolean | null;
  limpiarTelefono?: boolean | null;
  limpiarNumRegIdTrib?: boolean | null;
  limpiarPaisResidencia?: boolean | null;
  limpiarDomicilioExtranjeroCalle?: boolean | null;
  limpiarDomicilioExtranjeroEstado?: boolean | null;
  limpiarDomicilioExtranjeroCodigoPostal?: boolean | null;
}

export interface CrearClienteResponse {
  id: string;
  clave: string;
}

// ─── Productos A+W (ADR-0048) ──────────────────────────────────────

export interface ProductoAwItem {
  id: string;
  /** Correlación con A+W (referencia del producto); inmutable. */
  referenciaExterna: string;
  descripcion: string;
  /** Snapshot texto de la unidad (viene de A+W o del alta manual). */
  unidadMedida: string;
  /** FK al catálogo de unidades (ADR-0046); null = sin reconciliar. */
  unidadMedidaId: string | null;
  /** FK al catálogo de categorías (ADR-0046); null = sin categoría. */
  categoriaId: string | null;
  claveProdServSat: string | null;
  claveUnidadSat: string | null;
  objetoImp: string | null;
  tasaIvaTraslado: number | null;
  origen: OrigenMaster;
  /** false = falta clave prod/serv o clave unidad SAT → no timbra. */
  datosFiscalesCompletos: boolean;
  estatus: EstatusCatalogo;
}

export interface ListarProductosAwResponse {
  items: ProductoAwItem[];
  offset: number;
  limit: number;
  total: number;
}

export interface ProductoAwDetalle {
  id: string;
  referenciaExterna: string;
  descripcion: string;
  unidadMedida: string;
  unidadMedidaId: string | null;
  categoriaId: string | null;
  claveProdServSat: string | null;
  claveUnidadSat: string | null;
  objetoImp: string | null;
  tasaIvaTraslado: number | null;
  tasaRetencionIva: number | null;
  tasaRetencionIsr: number | null;
  /** Datos de aduana (CCE): fracción arancelaria, unidad aduanera, peso kg. */
  fraccionArancelaria: string | null;
  unidadAduana: string | null;
  pesoUnitarioKg: number | null;
  origen: OrigenMaster;
  datosFiscalesCompletos: boolean;
  estatus: EstatusCatalogo;
}

export interface CrearProductoAwPayload {
  referenciaExterna: string;
  descripcion: string;
  unidadMedida: string;
  unidadMedidaId: string | null;
  categoriaId: string | null;
  claveProdServSat: string | null;
  claveUnidadSat: string | null;
  objetoImp: string | null;
  tasaIvaTraslado: number | null;
  tasaRetencionIva: number | null;
  tasaRetencionIsr: number | null;
  fraccionArancelaria?: string | null;
  unidadAduana?: string | null;
  pesoUnitarioKg?: number | null;
}

export interface ActualizarProductoAwPayload {
  descripcion?: string | null;
  /** Solo aplica si NO viaja unidadMedidaId (el FK sincroniza el texto). */
  unidadMedida?: string | null;
  unidadMedidaId?: string | null;
  categoriaId?: string | null;
  claveProdServSat?: string | null;
  claveUnidadSat?: string | null;
  objetoImp?: string | null;
  tasaIvaTraslado?: number | null;
  tasaRetencionIva?: number | null;
  tasaRetencionIsr?: number | null;
  fraccionArancelaria?: string | null;
  unidadAduana?: string | null;
  pesoUnitarioKg?: number | null;
  limpiarCategoria?: boolean | null;
  limpiarTasaIvaTraslado?: boolean | null;
  limpiarTasaRetencionIva?: boolean | null;
  limpiarTasaRetencionIsr?: boolean | null;
  limpiarFraccionArancelaria?: boolean | null;
  limpiarUnidadAduana?: boolean | null;
  limpiarPesoUnitarioKg?: boolean | null;
}

export interface CrearProductoAwResponse {
  id: string;
  referenciaExterna: string;
}
