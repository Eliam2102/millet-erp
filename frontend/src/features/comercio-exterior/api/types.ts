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
  tipo: string;
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
export interface Filtros {
  tipo?: number;
  estado?: number;
  q?: string;
  offset?: number;
  sucursalId?: string;
}
