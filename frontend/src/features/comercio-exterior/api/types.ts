export const EstadoExpediente = { Abierto: 1, Cancelado: 2 } as const;
export const tiposParticipante = {
  1: 'Agente aduanal',
  2: 'Naviera',
  3: 'Aerolínea',
  4: 'Forwarder',
  5: 'Transportista terrestre',
  6: 'Broker',
  7: 'Cliente',
  8: 'Proveedor',
  9: 'Otro',
} as const;
export type TipoParticipante = keyof typeof tiposParticipante;

export type TipoExpediente = 1 | 2;
export interface ExpedienteFila {
  id: string;
  folio: string;
  tipo: TipoExpediente;
  referencia: string;
  sucursalId: string;
  sucursalNombre: string;
  estado: 1 | 2;
  revisionPendiente: boolean;
  moneda: string;
  responsableNombre: string;
  version: number;
  hitosAtrasados: number;
  avisos: number;
}
export interface Requisito {
  id: string;
  codigo: string;
  nombre: string;
  obligatorio: boolean;
  condicional: boolean;
}
export interface DocumentoVersion {
  id: string;
  tipoDocumentoId: string;
  numeroVersion: number;
  adjuntoId: string | null;
  reemplazaA: string | null;
  motivo: string | null;
  noAplica: boolean;
  estado: 1 | 2;
  responsableNombre: string;
  createdAt: string;
}
export interface Participante {
  id: string;
  nombre: string;
  tipo: TipoParticipante;
  descripcion: string | null;
  contacto: string | null;
  papel: 1 | 2 | 3;
  proveedorId: string | null;
  clienteId: string | null;
}
export interface ExpedienteDetalle {
  expediente: ExpedienteFila;
  ocId: string | null;
  ocFolio: string | null;
  informacionImportacion: {
    paisOrigen: string | null;
    numeroContenedor: string | null;
    codigoRuta: string | null;
    numeroPedimento: string | null;
  } | null;
  incotermId: string;
  incotermCodigo: string;
  responsableUsuarioId: string;
  tc: number | null;
  fechaTc: string | null;
  fuenteTc: string | null;
  motivoCancelacion: string | null;
  revisadoPorId: string | null;
  participantes: Participante[];
  documentos: DocumentoVersion[];
  requisitos: Requisito[];
  faltantes: Requisito[];
  estadoDocumental: string;
  estadoFisico: string;
  estadoFinanciero: string;
  hitos: Hito[];
  avisos: AvisoHito[];
}
export interface CrearExpediente {
  tipo: TipoExpediente;
  ocId: string | null;
  sucursalId: string | null;
  referencia: string;
  incotermId: string | null;
  moneda: string | null;
  responsableUsuarioId: string;
  clientes: string[];
  tc: number | null;
  fechaTc: string | null;
  fuenteTc: string | null;
}
export interface OcImportacion {
  id: string;
  folio: string;
  moneda: string;
  sucursalId: string;
  informacionImportacion: { incotermId: string | null } | null;
}
export interface Pagina {
  items: ExpedienteFila[];
  total: number;
  offset: number;
  limit: number;
}
export interface Hito {
  id: string;
  clave: string;
  nombre: string;
  orden: number;
  fechaCompromiso: string | null;
  fechaReal: string | null;
  responsableUsuarioId: string;
  responsableNombre: string;
  evidenciaAdjuntoId: string | null;
  omitido: boolean;
  motivo: string | null;
  motivoCambioCompromiso: string | null;
  atrasado: boolean;
  diasAtraso: number;
}
export interface AvisoHito {
  id: string;
  hitoId: string;
  hitoNombre: string;
  fechaCompromiso: string;
  fechaAviso: string;
}
export interface Filtros {
  atraso?: boolean;
  tipo?: number;
  estado?: number;
  q?: string;
  offset?: number;
  sucursalId?: string;
}
