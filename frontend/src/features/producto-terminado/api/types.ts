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
}

export interface AvancePosicionDto {
  posicion: number;
  descripcion: string | null;
  esperadas: number;
  terminadas: number;
  /** Terminadas que no están cargadas ni entregadas. */
  enPt: number;
  /** 0 hasta que exista la carga (PT5.4+). */
  cargadas: number;
  /** 0 hasta que exista la entrega (PT5.4+). */
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
