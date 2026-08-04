/**
 * Tipos del API REST del módulo Catálogos — espejo de los DTOs del
 * backend en <c>Millet.Catalogos.Application.*</c> y los endpoints en
 * <c>Millet.Api.Endpoints.Catalogos.*</c> (UF-Admin-PR5).
 *
 * <para>Cubre 3 grupos:</para>
 * <list>
 *   <item><b>Grupo 1</b> — Monedas + TiposCambio (master-detail con
 *   histórico).</item>
 *   <item><b>Grupo 2</b> — Editables sin detalle (CondicionesPago,
 *   Incoterms, Transportistas, UsosPrincipales).</item>
 *   <item><b>Grupo 3</b> — SAT read-only (FormasPago, UsosCfdi,
 *   RegimenesFiscales).</item>
 * </list>
 */

// ─── Enums ─────────────────────────────────────────────────────────

/**
 * Estatus de un catálogo cross-empresa. Mirror del enum
 * <c>EstatusCatalogo</c> backend (<c>short</c>). El valor 2
 * (<c>EnRevision</c>) existe pero no se usa en los catálogos editables
 * de este PR — se incluye por consistencia con el enum de Datos
 * Maestros.
 */
export const EstatusCatalogo = {
  Activo: 0,
  Inactivo: 1,
  EnRevision: 2,
} as const;
export type EstatusCatalogo =
  (typeof EstatusCatalogo)[keyof typeof EstatusCatalogo];

/**
 * Origen del valor de tipo de cambio. Mirror de
 * <c>Millet.Catalogos.Domain.OrigenTipoCambio</c>: <c>Manual</c> es el
 * default (admin lo capturó a mano); <c>DOF</c> y <c>Banxico</c> quedan
 * como hooks para futuros importers.
 */
export const OrigenTipoCambio = {
  Manual: 0,
  DOF: 1,
  Banxico: 2,
} as const;
export type OrigenTipoCambio =
  (typeof OrigenTipoCambio)[keyof typeof OrigenTipoCambio];

/**
 * A qué tipo de persona aplica un uso CFDI. Mirror de
 * <c>Millet.Catalogos.Domain.AplicaTipoPersona</c>.
 */
export const AplicaTipoPersona = {
  AmbosFisicaMoral: 0,
  SoloFisica: 1,
  SoloMoral: 2,
} as const;
export type AplicaTipoPersona =
  (typeof AplicaTipoPersona)[keyof typeof AplicaTipoPersona];

// ─── Grupo 1: Monedas + TiposCambio ────────────────────────────────

export interface MonedaResponse {
  id: string;
  codigo: string;
  nombre: string;
  decimales: number;
  activa: boolean;
  version: number;
}

export interface CrearMonedaPayload {
  codigo: string;
  nombre: string;
  decimales: number;
  activa: boolean;
}

export interface ActualizarMonedaPayload {
  nombre?: string | null;
  decimales?: number | null;
  activa?: boolean | null;
}

export interface TipoCambioResponse {
  id: string;
  monedaId: string;
  /** ISO <c>YYYY-MM-DD</c> (DateOnly del backend). */
  fecha: string;
  valorEnMxn: number;
  origen: OrigenTipoCambio;
  version: number;
}

export interface ListarTiposCambioResponse {
  items: TipoCambioResponse[];
  offset: number;
  limit: number;
  total: number;
}

export interface RegistrarTipoCambioPayload {
  /** ISO <c>YYYY-MM-DD</c>. */
  fecha: string;
  valorEnMxn: number;
  origen: OrigenTipoCambio;
}

// ─── Grupo 2: Catálogos editables sin detalle ──────────────────────

export interface CondicionesPagoResponse {
  id: string;
  clave: string;
  nombre: string;
  diasCredito: number;
  estatus: EstatusCatalogo;
  version: number;
}

export interface CrearCondicionesPagoPayload {
  clave: string;
  nombre: string;
  diasCredito: number;
}

export interface ActualizarCondicionesPagoPayload {
  nombre?: string | null;
  diasCredito?: number | null;
}

export interface IncotermResponse {
  id: string;
  codigo: string;
  nombre: string;
  estatus: EstatusCatalogo;
  version: number;
}

export interface CrearIncotermPayload {
  codigo: string;
  nombre: string;
}

export interface ActualizarIncotermPayload {
  nombre?: string | null;
}

// ─── Unidades de medida (ADR-0046 Etapa 1a) ────────────────────────
export const DimensionUnidad = {
  Conteo: 0,
  Peso: 1,
  Volumen: 2,
  Longitud: 3,
  Tiempo: 4,
} as const;
export type DimensionUnidad =
  (typeof DimensionUnidad)[keyof typeof DimensionUnidad];

export const DIMENSION_UNIDAD_LABEL: Record<DimensionUnidad, string> = {
  [DimensionUnidad.Conteo]: 'Conteo',
  [DimensionUnidad.Peso]: 'Peso',
  [DimensionUnidad.Volumen]: 'Volumen',
  [DimensionUnidad.Longitud]: 'Longitud',
  [DimensionUnidad.Tiempo]: 'Tiempo',
};

export interface UnidadMedidaResponse {
  id: string;
  codigo: string;
  nombre: string;
  dimension: DimensionUnidad;
  factorABase: number;
  decimales: number;
  esBase: boolean;
  estatus: EstatusCatalogo;
  version: number;
}

export interface CrearUnidadMedidaPayload {
  codigo: string;
  nombre: string;
  dimension: DimensionUnidad;
  factorABase: number;
  decimales: number;
  esBase: boolean;
}

export interface ActualizarUnidadMedidaPayload {
  nombre?: string | null;
  decimales?: number | null;
  dimension?: DimensionUnidad | null;
  factorABase?: number | null;
  esBase?: boolean | null;
}

export interface TransportistaResponse {
  id: string;
  clave: string;
  nombre: string;
  email: string | null;
  telefono: string | null;
  estatus: EstatusCatalogo;
  version: number;
}

export interface CrearTransportistaPayload {
  clave: string;
  nombre: string;
  email: string | null;
  telefono: string | null;
}

export interface ActualizarTransportistaPayload {
  nombre?: string | null;
  email?: string | null;
  telefono?: string | null;
  limpiarEmail?: boolean | null;
  limpiarTelefono?: boolean | null;
}

export interface UsoPrincipalResponse {
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
  version: number;
}

export interface CrearUsoPrincipalPayload {
  clave: string;
  nombre: string;
}

export interface ActualizarUsoPrincipalPayload {
  nombre?: string | null;
}

// ─── Categoría de artículo (patrón ADR-0046) ───────────────────────

export interface CategoriaArticuloResponse {
  id: string;
  nombre: string;
  estatus: EstatusCatalogo;
  version: number;
}

export interface CrearCategoriaArticuloPayload {
  nombre: string;
}

export interface ActualizarCategoriaArticuloPayload {
  nombre?: string | null;
}

// ─── Grupo 3: SAT read-only ────────────────────────────────────────

export interface FormaPagoItem {
  id: string;
  claveSat: string;
  descripcion: string;
  activa: boolean;
}

export interface UsoCfdiItem {
  id: string;
  claveSat: string;
  descripcion: string;
  aplicaTipoPersona: AplicaTipoPersona;
  activa: boolean;
}

export interface RegimenFiscalItem {
  id: string;
  codigo: string;
  nombre: string;
  aplicaPersonaFisica: boolean;
  estatus: EstatusCatalogo;
}
