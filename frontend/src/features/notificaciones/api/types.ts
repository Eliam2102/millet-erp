export interface AvisoUsuario {
  claveOcurrencia: string;
  modulo: string;
  tipoDocumento: string;
  documentoId: string;
  folio: string | null;
  titulo: string;
  motivo: string | null;
  fechaOcurrencia: string;
  categoria: 'Accion' | 'Informativo';
  destino: string;
  sucursalId: string | null;
  leidoOrigenAt: string | null;
}
export interface CentroAvisosRespuesta {
  contexto: { usuarioId: string; empresaId: string; sucursalId: string | null };
  fuentes: { id: string; estado: 'Complete' | 'Failed'; mensaje?: string | null }[];
  coberturaCompleta: boolean;
  totalAccionesConocidas: number;
  totalSinLeerConocido: number;
  totalInformativosConocidos: number;
  totalFiltradoConocido: number;
  siguienteContinuacion: string | null;
  items: { aviso: AvisoUsuario; leido: boolean }[];
  novedades: { hayNovedades: boolean; cantidadConocida: number; hastaSecSeguro: string; porFuente: { id: string; cantidad: number }[] };
}
export interface FiltrosAvisos {
  condicion: 'Todas' | 'SinLeer' | 'PorHacer';
  modulo?: string;
  continuacion?: string;
}
