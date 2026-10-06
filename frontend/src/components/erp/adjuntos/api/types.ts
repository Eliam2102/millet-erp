/**
 * Tipos del servicio genérico de adjuntos (F1-ADM-11 G1.2). Espejo de
 * `Millet.Compartido.Application.Adjuntos`. Los enums del backend viajan
 * como NÚMERO en JSON. Ningún DTO trae blobUrl/blobRef: la descarga va por
 * enlace temporal (`useEnlaceAdjunto`).
 */

/** Estado derivado de un adjunto (EstadoAdjunto.cs). */
export const EstadoAdjunto = {
  Vigente: 1,
  PorVencer: 2,
  Vencido: 3,
  SinVigencia: 4,
  Baja: 5,
} as const;
export type EstadoAdjunto = (typeof EstadoAdjunto)[keyof typeof EstadoAdjunto];

/** Estado de un tipo de documento dentro del expediente (EstadoExpedienteDocumento). */
export const EstadoExpediente = {
  Faltante: 0,
  Vigente: 1,
  PorVencer: 2,
  Vencido: 3,
} as const;
export type EstadoExpediente =
  (typeof EstadoExpediente)[keyof typeof EstadoExpediente];

export interface AdjuntoResponse {
  id: string;
  tipoEntidad: string;
  entidadId: string;
  tipoDocumentoId: string;
  tipoDocumentoCodigo: string;
  tipoDocumentoNombre: string;
  nombreArchivo: string;
  contentType: string;
  tamanoBytes: number;
  hashSha256: string;
  /** Fecha (yyyy-MM-dd) o null si el tipo no maneja vigencia. */
  vigenteHasta: string | null;
  estado: EstadoAdjunto;
  subidoPorId: string;
  subidoEn: string;
  bajaEn: string | null;
  bajaPorId: string | null;
  bajaMotivo: string | null;
}

export interface TipoDocumentoAdjunto {
  id: string;
  codigo: string;
  nombre: string;
  orden: number;
  obligatorio: boolean;
  vigenciaMeses: number | null;
  soloPersonaMoral: boolean;
}

export interface ExpedienteDocumento {
  tipoDocumentoId: string;
  codigo: string;
  nombre: string;
  obligatorio: boolean;
  vigenciaMeses: number | null;
  estado: EstadoExpediente;
  actual: AdjuntoResponse | null;
}

export interface Expediente {
  tipoEntidad: string;
  entidadId: string;
  completo: boolean;
  documentos: ExpedienteDocumento[];
  faltantes: string[];
  vencidos: string[];
  porVencer: string[];
}

export interface EnlaceDescarga {
  /** Ruta relativa al API; expira en ~60 s. */
  url: string;
  expiraEn: string;
}

export interface SubirAdjuntoArgs {
  archivo: File;
  tipoDocumentoId: string;
  /** yyyy-MM-dd; opcional (el backend calcula la vigencia por defecto). */
  vigenteHasta?: string | null;
  /** UUID por intento (ADR-0020). Si falta se genera uno. */
  idempotencyKey?: string;
  signal?: AbortSignal;
}
