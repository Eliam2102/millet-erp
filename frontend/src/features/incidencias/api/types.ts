/**
 * Espejo del contrato v1 de `/api/v1/incidencias` (U-4). Los estados viajan
 * como texto. «Pendiente» = `Abierta` o `EnAtencion`.
 */
export const EstadoIncidencia = {
  Abierta: 'Abierta',
  EnAtencion: 'EnAtencion',
  Resuelta: 'Resuelta',
  Cancelada: 'Cancelada',
} as const;
export type EstadoIncidencia = (typeof EstadoIncidencia)[keyof typeof EstadoIncidencia];

export const ESTADOS_PENDIENTES: EstadoIncidencia[] = [EstadoIncidencia.Abierta, EstadoIncidencia.EnAtencion];

export interface IncidenciaFila {
  id: string;
  numero: number;
  modulo: string;
  tipoDocumento: string;
  documentoId: string;
  documentoEtiqueta: string;
  sucursalId: string | null;
  paso: string;
  pasoNombre: string;
  codigoMotivo: string | null;
  motivo: string;
  estado: EstadoIncidencia;
  responsableUsuarioId: string;
  responsableNombre: string;
  abiertaPorNombre: string;
  automatica: boolean;
  abiertaEn: string;
  fechaCompromiso: string | null;
  rutaReanudacion: string | null;
  version: number;
}

export type AccionHistorial = 'Abierta' | 'Atendida' | 'Resuelta' | 'Cancelada' | 'Reasignada';

export interface HistorialIncidencia {
  en: string;
  accion: AccionHistorial;
  estadoAnterior: EstadoIncidencia | null;
  estadoNuevo: EstadoIncidencia;
  usuarioNombre: string;
  nota: string | null;
}

export interface IncidenciaDetalle extends IncidenciaFila {
  detalle: string | null;
  detalleClave: string | null;
  pasoReanudacion: string | null;
  pasoReanudacionNombre: string | null;
  abiertaPorId: string | null;
  resueltaPorNombre: string | null;
  resueltaEn: string | null;
  resolucion: string | null;
  puedeAtender: boolean;
  puedeResolver: boolean;
  puedeCancelar: boolean;
  puedeReasignar: boolean;
  historial: HistorialIncidencia[];
}

export interface PaginaIncidencias {
  items: IncidenciaFila[];
  total: number;
  offset: number;
  limit: number;
}

export interface ResumenMisIncidencias {
  abiertas: number;
  enAtencion: number;
}

export interface PasoIncidencia {
  clave: string;
  nombre: string;
  modulo: string;
}

export interface AbrirIncidenciaRequest {
  modulo: string;
  tipoDocumento: string;
  documentoId: string;
  documentoEtiqueta: string;
  sucursalId: string | null;
  paso: string;
  codigoMotivo: string | null;
  motivo: string;
  detalle: string | null;
  detalleClave: string | null;
  responsableUsuarioId: string;
  fechaCompromiso: string | null;
  pasoReanudacion: string | null;
  rutaReanudacion: string | null;
}

/** Documento sobre el que se trabaja: lo que la UI sabe al registrar desde un error o consultar por documento. */
export interface DocumentoIncidencia {
  modulo: string;
  tipoDocumento: string;
  documentoId: string;
  documentoEtiqueta: string;
  sucursalId?: string | null;
}

/** Contexto para registrar una incidencia desde la pantalla donde ocurrió el rechazo. */
export interface ContextoIncidencia extends DocumentoIncidencia {
  /** Clave estable del paso (p. ej. `alm.ubicaciones.asignacion`). */
  paso: string;
  /** Responsable sugerido; si falta, el usuario lo elige. */
  responsableUsuarioId?: string | null;
  responsableNombre?: string;
  detalleClave?: string | null;
  pasoReanudacion?: string | null;
  /** Ruta de la app donde se retoma; por omisión, la pantalla actual. */
  rutaReanudacion?: string | null;
}
