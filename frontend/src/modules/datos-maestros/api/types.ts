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
  validadoEn?: string | null;
  motivoRechazo?: string | null;
}

export interface ListarProveedoresResponse {
  items: ProveedorItem[];
  offset: number;
  limit: number;
  total: number;
}

export interface ProveedorDetalle {
  calle?: string | null;
  numeroExterior?: string | null;
  numeroInterior?: string | null;
  colonia?: string | null;
  ciudad?: string | null;
  municipio?: string | null;
  estado?: string | null;
  codigoPostal?: string | null;
  pais?: string | null;
  taxId?: string | null;
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
  toleranciaFacturaContraOcMxn: number | null;
  validadoEn: string | null;
  motivoRechazo: string | null;
  /** 0 = Nacional, 1 = Extranjero, null = por revisar. */
  origen: OrigenProveedor | null;
  regimenFiscalCodigo: string | null;
  /** Concepto de retención esperado; `SIN_RETENCION` = sin retención; null = por revisar. */
  conceptoRetencion: string | null;
  /** Versión de la matriz del expediente con la que se validó el proveedor. */
  validadoConMatrizVersion?: number | null;
}

export const OrigenProveedor = { Nacional: 0, Extranjero: 1 } as const;
export type OrigenProveedor = (typeof OrigenProveedor)[keyof typeof OrigenProveedor];

export interface ClasificacionFiscalProveedorPayload {
  origen: OrigenProveedor | null;
  regimenFiscalCodigo: string | null;
  conceptoRetencion: string | null;
}

export interface CrearProveedorPayload {
  calle?: string | null;
  numeroExterior?: string | null;
  numeroInterior?: string | null;
  colonia?: string | null;
  ciudad?: string | null;
  municipio?: string | null;
  estado?: string | null;
  codigoPostal?: string | null;
  pais?: string | null;
  taxId?: string | null;
  clave: string;
  razonSocial: string;
  /** Opcional solo si el origen es Extranjero: el backend guarda el RFC genérico XEXX010101000. */
  rfc: string | null;
  tipoPersona: TipoPersonaProveedor;
  nombreComercial: string | null;
  condicionesPagoDias: number | null;
  monedaPreferidaId: string | null;
  email: string | null;
  telefono: string | null;
  /** Clasificación fiscal en la misma transacción del alta (86e3pdmee). */
  origen?: OrigenProveedor | null;
  regimenFiscalCodigo?: string | null;
  conceptoRetencion?: string | null;
}

export interface ActualizarProveedorPayload {
  calle?: string | null;
  numeroExterior?: string | null;
  numeroInterior?: string | null;
  colonia?: string | null;
  ciudad?: string | null;
  municipio?: string | null;
  estado?: string | null;
  codigoPostal?: string | null;
  pais?: string | null;
  taxId?: string | null;
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
  /**
   * <c>EnRevision</c> cuando el alta usó un RFC genérico y coincide con
   * la razón social de otro proveedor existente (posible duplicado sin
   * fusionar automáticamente).
   */
  estatus: EstatusCatalogo;
  /** Id del proveedor con el que se detectó la posible duplicidad. */
  posibleDuplicadoDeId: string | null;
}

/** Estado de una cuenta bancaria de proveedor (enum del backend, viaja como número). */
export const EstadoCuentaBancariaProveedor = {
  PorValidar: 0,
  Activa: 1,
  Inhabilitada: 2,
  Rechazada: 3,
} as const;
export type EstadoCuentaBancariaProveedor =
  (typeof EstadoCuentaBancariaProveedor)[keyof typeof EstadoCuentaBancariaProveedor];

export const EstadoCuentaBancariaProveedorLabels: Record<
  EstadoCuentaBancariaProveedor,
  string
> = {
  0: 'Por validar',
  1: 'Activa',
  2: 'Inhabilitada',
  3: 'Rechazada',
};

/**
 * Respuesta de <c>GET /api/v1/datos-maestros/proveedores/{id}/cuentas-bancarias</c>.
 * <c>numero</c> llega enmascarado salvo que <c>numeroCompleto = true</c>.
 */
export interface CuentaBancariaProveedor {
  id: string;
  banco: string;
  numero: string;
  esClabe: boolean;
  swift: string | null;
  moneda: 'MXN' | 'USD';
  titular: string;
  predeterminada: boolean;
  estado: EstadoCuentaBancariaProveedor;
  capturadaPorNombre: string | null;
  capturadaEn: string;
  capturadaPorMi: boolean;
  validadaPorNombre: string | null;
  validadaEn: string | null;
  documentoAdjuntoId: string | null;
  notaValidacion: string | null;
  motivoEstado: string | null;
  numeroCompleto: boolean;
  version: number;
  /** Quién y cuándo rechazó, inhabilitó o reactivó la cuenta por última vez. */
  ultimoCambioPorNombre?: string | null;
  ultimoCambioEn?: string | null;
  /** Solo en la respuesta del alta: la misma cuenta existe en otro proveedor. */
  advertencia?: string | null;
}

export interface AgregarCuentaBancariaProveedorPayload {
  banco: string;
  clabe?: string | null;
  numeroCuenta?: string | null;
  swift?: string | null;
  moneda: 'MXN' | 'USD';
  titular: string;
}

export interface ValidarCuentaBancariaProveedorPayload {
  documentoAdjuntoId: string;
  cotejoCompleto: boolean;
  nota?: string | null;
}

export type AccionCuentaBancariaProveedor =
  | 'rechazar'
  | 'inhabilitar'
  | 'reactivar'
  | 'predeterminada';

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
  /** Familia A+W del vidrio crudo (E07 D5, 1:1); null = insumo sin medidas. */
  productoAwId?: string | null;
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
  /** Si llega, liga el artículo a esa familia A+W (rechaza si ya está ligada a otro o hay existencia). */
  productoAwId?: string | null;
  limpiarProductoAw?: boolean | null;
}

export interface CrearArticuloResponse {
  id: string;
  clave: string;
}

// ─── Clientes (ADR-0048) ───────────────────────────────────────────

/** Mirror de <c>ResultadoSincronizacionAw</c> (serializado como string). */
export type ResultadoSincronizacion =
  | 'Aplicado'
  | 'SinCambios'
  | 'Pendiente'
  | 'Conflicto'
  | 'Error';

export interface ClienteOrigenAwResumen {
  resultado: ResultadoSincronizacion;
  ultimaLecturaUtc: string;
}

/** Candidatos fiscales/crédito/domicilio: solo con <c>origen-ver</c> (null si no). */
export interface ClienteOrigenAwDetalle {
  condicionOrigen: string | null;
  diasNominalesOrigen: number | null;
  monedaOrigen: string | null;
  monedaNormalizada: string | null;
  nombreComercialOrigen: string | null;
  estadoOrigenCrudo: number | null;
  bloqueoOrigenCrudo: number | null;
  ultimaLecturaUtc: string;
  ultimaAplicacionUtc: string | null;
  resultado: ResultadoSincronizacion;
  error: string | null;
  versionContrato: string;
  versionMapeo: string;
  registroVersion: number;
  candidatoFiscalUstId?: string | null;
  candidatoFiscalSteuernummer?: string | null;
  creditoReferenciaLimite?: number | null;
  creditoReferenciaLimite1?: number | null;
  creditoReferenciaNet?: number | null;
  domicilioOrigenCalle?: string | null;
  domicilioOrigenCiudad?: string | null;
  domicilioOrigenCp?: string | null;
  domicilioOrigenProvincia?: string | null;
  domicilioOrigenPais?: string | null;
  /** Recibido de A+W que no se aplicó al cliente existente (última aplicación). */
  diferencias?: DiferenciaAplicacionAw[];
}

export interface DiferenciaAplicacionAw {
  campo: string;
  recibido?: string | null;
  conservado?: string | null;
  motivo: string;
}

// ─── Sincronización de clientes A+W (F1-ADM-06) ─────────────────────

export type EstadoEjecucionSync =
  | 'Pendiente'
  | 'EnCurso'
  | 'Completa'
  | 'Parcial'
  | 'Fallida'
  | 'Cancelada';

/** GET …/clientes/sincronizacion/origen/{referencia}: cómo llegó de A+W y qué le falta. */
export interface ClienteAwOrigenDetalle {
  referencia: string;
  campos: { campo: string; etiqueta: string; valor: string | null }[];
  faltantes: {
    campo: string;
    etiqueta: string;
    regla: string;
    comoDebeQuedar: string;
    /** Área que valida el dato correcto antes de corregirlo. */
    area: string;
    corrigeEn: string;
  }[];
}

export interface EjecucionSyncErrorItem {
  referencia: string;
  codigo: string;
  mensaje: string;
}

export interface EjecucionSyncResumen {
  id: string;
  tipo: string;
  estado: EstadoEjecucionSync;
  leidos: number;
  creados: number;
  actualizados: number;
  sinCambios: number;
  pendientes: number;
  conflictos: number;
  errores: number;
  iniciadaEnUtc: string | null;
  terminadaEnUtc: string | null;
  actor: string | null;
  reintentoDeId: string | null;
  errorGeneral: string | null;
  /** JSON del barrido de productos de la corrida (ADM-06 R4); null si la ejecución no es una corrida. */
  resumenProductos?: string | null;
}

export interface EjecucionSyncDetalle
  extends Omit<EjecucionSyncResumen, 'errores'> {
  errores: EjecucionSyncErrorItem[];
  /** El backend limita cuántos errores lista; true = hay más de los mostrados. */
  erroresTruncados?: boolean;
}

/** Forma real del backend (GET /ejecuciones/{id} y POST /reintentos): el resumen viene anidado. */
export interface EjecucionSyncDetalleRespuesta {
  ejecucion: Omit<EjecucionSyncResumen, 'errores'>;
  errores: EjecucionSyncErrorItem[];
  erroresTruncados: boolean;
}

export interface ListarEjecucionesSyncResponse {
  items: EjecucionSyncResumen[];
  offset: number;
  limit: number;
  total: number;
}

export interface EjecucionSyncAceptada {
  id: string;
  estado: EstadoEjecucionSync;
}

export interface ClienteItem {
  clasificacionId?: string | null;
  clasificacionNombre?: string | null;
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
  /** Resumen de la última lectura A+W; null si nunca se sincronizó. */
  origenAw?: ClienteOrigenAwResumen | null;
}

export interface ListarClientesResponse {
  items: ClienteItem[];
  offset: number;
  limit: number;
  total: number;
}

export interface ClienteDetalle {
  clasificacionId?: string | null;
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
  version: number;
  origenAw?: ClienteOrigenAwDetalle | null;
}

export interface CrearClientePayload {
  clasificacionId?: string | null;
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
  clasificacionId?: string | null;
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
  /** Fecha de baja (A+W o manual); null = nunca dado de baja. */
  fechaBaja?: string | null;
  numVariantes?: number;
  /** Tipo de A+W (Vidrio plano, VTE, VLA, VC…); null = sin dato. */
  tipo?: string | null;
  /** Piezas del árbol de composición (0 = sin composición). */
  numComponentes?: number;
  /** PIN-2: marca explícita «Servicio de recubrimiento» (dato del ERP; A+W no la sobrescribe). */
  esServicioRecubrimiento?: boolean;
}

export interface ListarProductosAwResponse {
  items: ProductoAwItem[];
  offset: number;
  limit: number;
  total: number;
}

export interface ProductoAwDetalle {
  origenFiscal?: number | string;
  estadoFiscal?: number | string;
  reglaFiscalId?: string | null;
  confirmadoPor?: string | null;
  confirmadoEn?: string | null;
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
  fechaBaja?: string | null;
  /** PIN-2: marca «Servicio de recubrimiento». */
  esServicioRecubrimiento?: boolean;
  variantes?: ProductoAwVariante[];
  /** Clasificación de A+W (dueño A+W; null = sin dato). Familia de negocio = tipo → grupo; wgr = grupo de mercancía (`KA_WGR`). */
  codigoModelo?: string | null;
  grupo?: string | null;
  tipo?: string | null;
  wgr?: string | null;
  wgrDescripcion?: string | null;
  /** Árbol de composición aplanado, ordenado por `orden`. */
  componentes?: ProductoAwComponente[];
  /** Versión (ETag) para If-Match en la edición. */
  version: number;
}

/** Pieza del árbol de composición; `padreOrden` null = raíz, `nivel` >= 1. */
export interface ProductoAwComponente {
  orden: number;
  nivel: number;
  padreOrden: number | null;
  componenteRef: string;
  descripcion: string | null;
  tipo: string | null;
  espesorMm: number | null;
}

/** Medidas en mm: null = A+W no las informó (NUNCA 0). */
export interface ProductoAwVariante {
  /** Id de la variante (E07 D3); lo viajan las líneas de movimiento y el saldo. */
  id?: string;
  claveVariante: string;
  altoMm: number | null;
  anchoMm: number | null;
  espesorMm: number | null;
  composicion: string | null;
}

// ─── Sincronización de productos A+W (F1-ADM-07) ────────────────────

export interface AwProductoError {
  referencia: string;
  codigo: string;
  mensaje: string;
}

/** Resumen síncrono del barrido o del reintento por referencia. */
export interface AwProductosResumen {
  leidos: number;
  creados: number;
  actualizados: number;
  sinCambios: number;
  pendientes: number;
  conflictos: number;
  errores: number;
  erroresPorReferencia: AwProductoError[];
}

export interface ProductoAwSincronizacionDetalle {
  resultado: ResultadoSincronizacion | null;
  error: string | null;
  /** JSON serializado por el backend (lista de DiferenciaAplicacionAw). */
  diferencias: string | null;
  hashOrigen: string | null;
  leidoEnUtc: string | null;
  aplicadoEnUtc: string | null;
  versionContrato: string | null;
  versionMapeo: string | null;
  descripcionOrigen: string | null;
  unidadOrigenCruda: string | null;
  bajaOrigenCruda: string | null;
}

export interface ProductoAwSincronizacionEstado {
  productoId: string;
  referencia: string;
  version: number;
  /** null = el producto nunca vino de A+W. */
  sincronizacion: ProductoAwSincronizacionDetalle | null;
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
  esServicioRecubrimiento?: boolean;
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
  /** null/ausente = sin cambio. */
  esServicioRecubrimiento?: boolean | null;
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
