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

/** Preparado (documentado, sin mover piezas) → EnTransito (autorizado por otra persona) → ConDiferencia → Recibido o Cerrado (diferencias aclaradas). */
export type EstadoTraspaso = 'Preparado' | 'EnTransito' | 'ConDiferencia' | 'Recibido' | 'Cerrado';

export type ClasificacionDiferencia = 'Faltante' | 'DanadaEnTransito' | 'EnCuarentena';
export type ResolucionDiferencia = 'RecibidaTarde' | 'RegresadaAlOrigen' | 'DadaPorPerdida';

export interface TraspasoPiezaDto {
  piezaId: string;
  etiqueta: string;
  numeroPedido: string;
  recibida: boolean;
  clasificacion: ClasificacionDiferencia | null;
  motivo: string | null;
  evidencia: string | null;
  resolucion: ResolucionDiferencia | null;
}

/** `piezas` solo viene en el detalle; `pendientes` = piezas aún en tránsito (sin llegar ni aclarar). */
export interface TraspasoDto {
  id: string;
  folio: string;
  sucursalOrigenId: string;
  sucursalDestinoId: string;
  estado: EstadoTraspaso;
  salidaAt: string;
  recibidoAt: string | null;
  total: number;
  pendientes: number;
  piezas: TraspasoPiezaDto[];
  preparadoPorId: string;
  autorizadoPorId: string | null;
  autorizadoAt: string | null;
  foranea: boolean;
  destino: string | null;
  cartaPorteFolio: string | null;
}

/** Datos de transporte del traspaso foráneo (los mismos de la hoja de salida, para la Carta Porte). */
export interface TransporteTraspasoBody {
  destino: string;
  vehiculoId: string | null;
  operadorId: string | null;
  distanciaKm: number;
  pesoKg: number;
  origenCodigoPostal: string;
  origenEstado: string;
  destinoCodigoPostal: string;
  destinoEstado: string;
}

export interface CrearTraspasoBody {
  sucursalOrigenId: string;
  sucursalDestinoId: string;
  etiquetas: string[];
  foranea?: boolean;
  transporte?: TransporteTraspasoBody;
}

export interface DiferenciaTraspasoBody {
  piezaId: string;
  clasificacion: ClasificacionDiferencia;
  motivo: string;
  evidencia?: string | null;
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
  /** PT5.4: piezas del pedido, las que se pueden cargar todavía (solo en carga) y las rotas pendientes de reposición. */
  esperadas?: number;
  disponibles?: string[];
  rotas?: string[];
  /** PT5.6: confirmación de la entrega (null mientras falta) y estado del pedido (`71` en ruta, `Entregado` sin factura, `115` facturado y entregado). */
  entrega?: EntregaDto | null;
  estadoPedido?: '71' | 'Entregado' | '115' | null;
}

export type ResultadoEntrega = 'Entregada' | 'Parcial' | 'Rechazada';
export type FormaCobroEntrega = 'TPV' | 'Efectivo' | 'Transferencia' | 'Cheque';

/** Cobro contra entrega del chofer; `conciliadoAt` se llena cuando la caja liquida la ruta y el voucher coincide. */
export interface CobroEntregaDto {
  monto: number;
  formaPago: FormaCobroEntrega;
  referencia: string | null;
  conciliadoAt?: string | null;
}

export interface EntregaDto {
  id: string;
  resultado: ResultadoEntrega;
  receptor: string;
  entregadaAt: string;
  entregadas: number;
  rechazadas: number;
  motivoRechazo: string | null;
  tieneFirma: boolean;
  tieneFoto: boolean;
  etiquetasRechazadas: string[];
  cobro: CobroEntregaDto | null;
}

/** Firma y foto como data URL (PNG la firma; JPEG o PNG la foto). Se necesita al menos una. */
export interface ConfirmarEntregaBody {
  receptor: string;
  rechazadas: string[];
  motivoRechazo: string | null;
  firma: string | null;
  foto: string | null;
  cobro: { monto: number; formaPago: FormaCobroEntrega; referencia: string | null } | null;
}

export interface EntregaEvidenciaDto {
  firma: string | null;
  foto: string | null;
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
  /** PT5.6: albaranes con piezas cuya entrega aún no se confirma (solo hojas emitidas). */
  entregasPendientes?: number;
  /** Reglas de salida (Millet, 9-nov): solo en el detalle de una hoja en carga. */
  reglas?: ReglasHojaDto | null;
}

/** Niveles de autorización de la hoja de salida, en orden de suplencia. */
export type NivelAutorizacionSalida = 'administracion' | 'planeacion' | 'contraloria' | 'credito' | 'ventas';

export interface AutorizacionSalidaDto {
  actor: string;
  fecha: string;
  nivel: NivelAutorizacionSalida;
  motivo: string;
  suplencia: string | null;
  /** «pedido:piezas cargadas» que cubre; si la carga cambia deja de cubrirlo. */
  alcance: string[];
}

/** Pedidos sin factura válida y pedidos que salen incompletos («pedido:piezas cargadas»), con su última autorización. */
export interface ReglasHojaDto {
  sinFactura: string[];
  parciales: string[];
  excepcionFactura: AutorizacionSalidaDto | null;
  parcial: AutorizacionSalidaDto | null;
  excepcionFacturaVigente: boolean;
  parcialVigente: boolean;
}

export interface AutorizarHojaBody {
  nivel: NivelAutorizacionSalida;
  motivo: string;
  suplencia: string | null;
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
  /** Sin etiquetas arma solo la ruta; las piezas se cargan después, una por una. */
  pedidos: { numeroPedido: string; etiquetas: string[] }[];
}
