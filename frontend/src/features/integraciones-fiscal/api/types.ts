/**
 * Tipos del módulo Integraciones.Fiscal (PR-8 frontend). Mirror manual
 * de los DTOs y commands que expone el backend en
 * <c>/api/v1/integraciones/fiscal/...</c>. Cuando ADR-0017 (codegen TS
 * desde OpenAPI) entre, este archivo se vuelve autogenerado.
 */

// ─── Proveedor del PAC ─────────────────────────────────────────────────

/** Hoy solo FiscalAPI (ADR-0038). Reservado el espacio para futuros. */
export const ProveedorPac = {
  FiscalApi: 1,
} as const satisfies Record<string, number>;
export type ProveedorPac = (typeof ProveedorPac)[keyof typeof ProveedorPac];

export const ProveedorPacLabels: Record<ProveedorPac, string> = {
  [ProveedorPac.FiscalApi]: 'FiscalAPI',
};

// ─── Configuración del PAC ─────────────────────────────────────────────

/**
 * Identidad de prueba (persona de la LCO sintética del SAT,
 * docs.fiscalapi.com/testing-data) que sustituye al emisor o receptor
 * real en el payload al PAC. Solo capturable con BaseUrl de sandbox.
 */
export interface IdentidadSandbox {
  rfc: string;
  razonSocial: string;
  regimenFiscal: string;
  codigoPostal: string;
}

export interface ConfiguracionPacResponse {
  id: string;
  empresaId: string;
  proveedor: ProveedorPac;
  proveedorNombre: string;
  baseUrl: string;
  /** Siempre "••••" — el plaintext nunca sale del backend. */
  apiKey: string;
  apiKeyConfigured: boolean;
  activo: boolean;
  ultimaRotacionAt: string | null;
  ultimaTestConexionAt: string | null;
  ultimaTestConexionExitosa: boolean | null;
  emisorSandbox: IdentidadSandbox | null;
  receptorSandbox: IdentidadSandbox | null;
  /** Las tres piezas del CSD (cer + key + password) están capturadas. */
  csdConfigurado: boolean;
  csdActualizadoAt: string | null;
  createdAt: string;
  updatedAt: string;
  version: number;
}

/**
 * CSD del emisor para la emisión "por valores" de FiscalAPI
 * (Issuer.TaxCredentials): archivos .cer y .key en base64 + password.
 * Solo viaja de entrada; el backend nunca lo regresa (solo
 * csdConfigurado).
 */
export interface CsdPayload {
  certificadoBase64: string;
  llavePrivadaBase64: string;
  password: string;
}

export interface GuardarConfiguracionPacPayload {
  baseUrl: string;
  /** Null = no rota la key actual. */
  apiKey: string | null;
  activo: boolean;
  emisorSandbox?: IdentidadSandbox | null;
  receptorSandbox?: IdentidadSandbox | null;
  /** Null = no toca el CSD persistido. */
  csd?: CsdPayload | null;
}

export interface TestConexionPacPayload {
  /** Credenciales transitorias — si se omiten, usa las persistidas. */
  baseUrl?: string | null;
  apiKey?: string | null;
  timeoutSegundos?: number | null;
}

export interface TestConexionPacResponse {
  exitosa: boolean;
  statusCode: number;
  mensaje: string;
  tiempoMs: number;
  consultadoEn: string;
}

// ─── RFCs receptores ───────────────────────────────────────────────────

export interface RfcReceptorResponse {
  id: string;
  empresaId: string;
  rfc: string;
  descargaHabilitada: boolean;
  refreshHabilitada: boolean;
  checkpointDescargaAt: string | null;
  /** PR-10: estado de la FIEL en FiscalAPI. */
  tieneFiel: boolean;
  fielValidFrom: string | null;
  fielValidTo: string | null;
  fielSubidaAt: string | null;
  createdAt: string;
  updatedAt: string;
  version: number;
}

export interface AgregarRfcReceptorCommand {
  empresaId: string;
  rfc: string;
}

export interface ActualizarRfcReceptorPayload {
  descargaHabilitada: boolean;
  refreshHabilitada: boolean;
}

/**
 * Body de <c>POST /api/v1/integraciones/fiscal/rfcs-receptores/{id}/fiel</c>.
 * Los archivos van como base64 dentro del JSON (no multipart) para
 * mantener consistencia con el resto del API + auth headers +
 * Idempotency-Key.
 */
export interface SubirFielReceptorPayload {
  legalName: string;
  zipCode: string;
  satTaxRegimeCode: string;
  email: string;
  cerBase64: string;
  keyBase64: string;
  password: string;
}
