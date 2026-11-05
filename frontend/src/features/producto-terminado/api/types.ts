/** Gemelos TypeScript de los DTOs de Producto Terminado (camelCase como los serializa la API). */
export type ResultadoEscaneo = 'Alta' | 'Pospuesto' | 'Excepcion' | 'Reposicion';

export interface PiezaDto {
  id: string;
  etiqueta: string;
  numeroPedido: string;
  posicion: number;
  numeroPieza: number;
  sucursalId: string;
  estado: string;
  escaneadaAt: string;
  /** Solo en tránsito: sucursal a la que va. */
  sucursalDestinoId?: string | null;
}

export interface AvancePosicionDto {
  posicion: number;
  descripcion: string | null;
  esperadas: number;
  terminadas: number;
  /** Terminadas que siguen en PT: ni cargadas ni entregadas. */
  enPt: number;
  /** Piezas en el camión (hoja de salida en borrador o carga de PT5.4). */
  cargadas: number;
  /** Piezas embarcadas con albarán emitido (PT5.5). */
  entregadas: number;
  /** Esperadas - terminadas. */
  pendientes: number;
  /** SinPiezas | Parcial | Completo. */
  avanceFisico: 'SinPiezas' | 'Parcial' | 'Completo';
}

/** Estado del pedido en el ERP (PT5.2), independiente del avance físico. No es el 69 de A+W. */
export interface EstadoPedidoDto {
  estado: 'Retenido' | 'Liberado';
  /** SaldoPendiente | SaldoCubierto | Reparto | DecisionCxc. */
  motivo: string | null;
  desde: string | null;
}

export interface PiezasPagina {
  items: PiezaDto[];
  total: number;
  avance: AvancePosicionDto[];
  /** Null hasta que el pedido está completo en PT (P-096): se muestra «En producción». */
  estadoPedido?: EstadoPedidoDto | null;
}

export interface EscaneoDto {
  id: string;
  origen: string;
  etiqueta: string;
  numeroPedido: string;
  posicion: number | null;
  numeroPieza: string | null;
  estacion: number;
  area: string | null;
  registradoAt: string;
  sucursalId: string | null;
  resultado: ResultadoEscaneo;
  motivo: string | null;
  intentos: number;
  piezaId: string | null;
  resueltoAt: string | null;
  motivoResolucion: string | null;
  version: number;
}

export interface EscaneosPagina {
  items: EscaneoDto[];
  total: number;
}

export type AlertaSalida = 'ninguna' | '24h' | '48h';

export interface SalidaPendienteDto {
  pedidoPtId: string;
  numeroPedido: string;
  cliente: string;
  destino: string | null;
  sucursalId: string;
  piezas: number;
  liberadoAt: string;
  horasSinSalida: number;
  alerta: AlertaSalida;
}

/** Con24h y Con48h cuentan todo lo filtrado por sucursal y búsqueda, antes de filtrar por alerta. */
export interface SalidasPendientesPagina {
  items: SalidaPendienteDto[];
  total: number;
  con24h: number;
  con48h: number;
}

export type EstadoTraspaso = 'EnTransito' | 'ConDiferencia' | 'Recibido';

export interface TraspasoPiezaDto {
  piezaId: string;
  etiqueta: string;
  numeroPedido: string;
  recibida: boolean;
}

/** `piezas` solo viene en el detalle; `pendientes` = piezas aún en tránsito. */
export interface TraspasoDto {
  id: string;
  sucursalOrigenId: string;
  sucursalDestinoId: string;
  estado: EstadoTraspaso;
  salidaAt: string;
  recibidoAt: string | null;
  total: number;
  pendientes: number;
  piezas: TraspasoPiezaDto[];
}

export interface TraspasosPagina {
  items: TraspasoDto[];
  total: number;
}

export type EstadoHojaSalida = 'Borrador' | 'Emitida' | 'Cancelada';

export interface AlbaranLineaDto {
  posicion: number;
  descripcion: string | null;
  piezas: number;
}

/** Albarán de un pedido en la hoja; `secuencia` es el orden de entrega. */
export interface AlbaranDto {
  id: string;
  folio: string;
  secuencia: number;
  numeroPedido: string;
  cliente: string;
  destino: string | null;
  emitidoAt: string | null;
  total: number;
  lineas: AlbaranLineaDto[];
  etiquetas: string[];
}

/** `albaranes` solo viene en el detalle; `yaEmitida` = se pidió emitir de nuevo y no cambió nada. */
export interface HojaSalidaDto {
  id: string;
  folio: string;
  sucursalId: string;
  destino: string;
  foranea: boolean;
  vehiculoId: string | null;
  operadorId: string | null;
  estado: EstadoHojaSalida;
  creadaAt: string;
  emitidaAt: string | null;
  cartaPorteFolio: string | null;
  pedidos: number;
  piezas: number;
  albaranes: AlbaranDto[];
  yaEmitida: boolean;
}

export interface HojasSalidaPagina {
  items: HojaSalidaDto[];
  total: number;
}

export interface CrearHojaSalidaBody {
  sucursalId: string;
  destino: string;
  foranea: boolean;
  vehiculoId?: string | null;
  operadorId?: string | null;
  distanciaKm?: number | null;
  pesoKg?: number | null;
  origenCodigoPostal?: string | null;
  origenEstado?: string | null;
  destinoCodigoPostal?: string | null;
  destinoEstado?: string | null;
  pedidos: { numeroPedido: string; etiquetas: string[] }[];
}
