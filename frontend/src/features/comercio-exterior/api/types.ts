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
  numeroPedimento?: string | null;
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
  incidencias?: Incidencia[];
  avisosIncidencias?: {
    id: string;
    incidenciaId: string;
    destinatarioUsuarioId: string;
    fecha: string;
  }[];
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
  conIncidencia?: boolean;
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

export interface Contenedor {
  id: string;
  numero: string | null;
  sello: string | null;
  booking: string | null;
  blGuia: string | null;
  ruta: string | null;
  fechaCargaPrevista: string | null;
  fechaEmbarqueReal: string | null;
  pesoKg: number | null;
  bultos: number | null;
  estado: 1 | 2;
  motivoCancelacion: string | null;
}
export type DatosContenedor = Omit<
  Contenedor,
  'id' | 'fechaEmbarqueReal' | 'estado' | 'motivoCancelacion'
>;
export interface PedimentoExportacion {
  aduana?: string | null;
  aduanaNombre?: string | null;
  numero: string;
  clave: string;
  fecha: string;
  documentoId?: string | null;
}
export interface PartidaEmbarcada {
  id: string;
  contenedorId: string;
  pedidoFacturableId: string;
  posicion: number;
  clienteId: string;
  cantidad: number;
  unidad: string;
  versionOrigenAsignada: number | null;
  estado: 1 | 2 | 3;
  confirmadoPorId: string;
}
export interface PedidoEmbarcable {
  id: string;
  numeroPedido: string;
  clienteId: string;
  clienteNombre: string;
  sucursalId: string;
  esExportacion: boolean;
  estado: string;
  versionOrigen: number | null;
  lineas: {
    posicion: number;
    productoId: string | null;
    descripcion: string;
    cantidad: number;
    unidad: string;
  }[];
  facturaVigente?: boolean;
  facturaFolio: string | null;
  facturaUuid: string | null;
}
export interface ContenedoresDetalle {
  aduanas: Record<string, string>;
  version: number;
  pedimento: PedimentoExportacion | null;
  contenedores: Contenedor[];
  partidas: PartidaEmbarcada[];
  pedidos: PedidoEmbarcable[];
}
export interface EmbarqueCe {
  aduana?: string | null;
  aduanaNombre?: string | null;
  ligado: boolean;
  expedienteId: string | null;
  sucursalId: string | null;
  listoParaFacturar: boolean;
  codigoBloqueo: string | null;
  motivo: string | null;
  folioExpediente: string | null;
  numeroPedimento: string | null;
  clavePedimento: string | null;
  fechaPedimento: string | null;
  incoterm: string | null;
  contenedores: { id: string; numero: string; blGuia: string; fechaEmbarqueReal: string }[] | null;
}
export interface DatosEmbarqueCe {
  aduana?: string | null;
  aduanaNombre?: string | null;
  folioExpediente: string;
  numeroPedimento: string;
  clavePedimento: string;
  fechaPedimento: string;
  incoterm: string;
  contenedores: { numero: string; blGuia: string; fechaEmbarqueReal: string }[];
}
export interface RelacionIva {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: { clave: string; etiqueta: string; tipo: 'texto' | 'fecha' }[];
  filas: {
    periodo: string;
    expediente: string;
    tipo: string;
    oc: string | null;
    pedimento: string | null;
    clave: string | null;
    bl: string | null;
    contenedor: string | null;
    cliente: string | null;
    pedido: string | null;
    factura: string | null;
    uuid: string | null;
    fechaEmbarque: string | null;
    entradaAlmacen: string | null;
  }[];
}

export const tiposIncidencia = {
  1: 'Cancelación de la operación',
  2: 'Rectificación de pedimento',
  3: 'Mercancía retenida',
  4: 'Mercancía rechazada',
  5: 'Mercancía dañada',
  6: 'Mercancía faltante',
  7: 'Rotura en transporte',
  8: 'Documento incorrecto',
  9: 'Otra',
} as const;
export const estadosIncidencia = {
  1: 'Abierta',
  2: 'En atención',
  3: 'Resuelta',
  4: 'Cancelada',
} as const;
export interface Incidencia {
  id: string;
  tipo: keyof typeof tiposIncidencia;
  estado: keyof typeof estadosIncidencia;
  fecha: string;
  responsableUsuarioId: string;
  responsableNombre: string;
  motivo: string;
  descripcion: string | null;
  participanteId: string | null;
  documentosIds: string[];
  evidenciasIds: string[];
  accionCorrectiva: string | null;
  efectoPosterior: string | null;
  hitoClave: string | null;
  contenedorId: string | null;
  partidaId: string | null;
  pedimentoAnterior: string | null;
  pedimentoNuevo: string | null;
  motivoCancelacion: string | null;
  antiguedadDias: number;
  historial: {
    estado: keyof typeof estadosIncidencia;
    actorId: string;
    fecha: string;
    motivo: string | null;
  }[];
}
export interface IncidenciasReporte {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: { clave: string; etiqueta: string; tipo: 'texto' | 'fecha' | 'numero' }[];
  filas: {
    expedienteId: string;
    expediente: string;
    sucursal: string;
    tipo: string;
    estado: string;
    fecha: string;
    motivo: string;
    responsable: string;
    antiguedadDias: number;
  }[];
}
