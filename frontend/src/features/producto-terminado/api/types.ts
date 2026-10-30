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

export interface PiezasPagina {
  items: PiezaDto[];
  total: number;
  avance: AvancePosicionDto[];
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
