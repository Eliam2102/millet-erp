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
  /** M-251: ubicación en almacén; `porUbicar` mientras nadie la asigna. */
  almacen?: AlmacenPt | null;
  lugar?: string | null;
  porUbicar?: boolean;
  /** Estación y hora A+W en que se terminó la pieza. */
  estacionOrigen?: number | null;
  areaOrigen?: string | null;
  terminadaAwAt?: string | null;
  /** Entró a PT sin costo asignado todavía. */
  costoPendiente?: boolean;
}

export type AlmacenPt = 'Nacional' | 'Exportacion';

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
  /** PT5.4: piezas del pedido, las que se pueden cargar todavía (solo en carga) y las rotas pendientes de reposición. */
  esperadas?: number;
  disponibles?: string[];
  rotas?: string[];
  /** PT5.6: confirmación de la entrega (null mientras falta) y estado del pedido (`71` en ruta, `Entregado` sin factura, `115` facturado y entregado). */
  entrega?: EntregaDto | null;
  estadoPedido?: '71' | 'Entregado' | '115' | null;
  cancelado?: boolean;
  /** M-247: cancelación pedida de un albarán emitido (null si nunca se pidió). */
  cancelacion?: CancelacionAlbaranDto | null;
}

export interface CancelacionAlbaranDto {
  pendiente: boolean;
  solicitadaAt: string | null;
  solicitadaPorId: string | null;
  motivoSolicitud: string | null;
  rechazadaAt: string | null;
  rechazadaPorId: string | null;
  motivoRechazo: string | null;
  autorizadaPorId: string | null;
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
  /** M-248: historial de la entrega (la última es la vigente). */
  versiones?: VersionEntregaDto[];
}

export interface VersionEntregaDto {
  numero: number;
  resultado: ResultadoEntrega;
  receptor: string;
  entregadas: number;
  rechazadas: number;
  motivoRechazo: string | null;
  entregadaAt: string;
  motivoCorreccion: string | null;
  corregidaAt: string | null;
  corregidaPor: string | null;
}

/** M-248: misma evidencia que al confirmar (firma o foto) más el motivo de la corrección. */
export interface CorregirEntregaBody {
  receptor: string;
  rechazadas: string[];
  motivoRechazo: string | null;
  firma: string | null;
  foto: string | null;
  motivoCorreccion: string;
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

export interface UbicarPiezasBody {
  etiquetas: string[];
  almacen: AlmacenPt;
  lugar?: string;
}

/** M-245: salida parcial de un pedido que aún no está completo en PT. */
export type EstadoSolicitudParcial = 'Pendiente' | 'Autorizada' | 'Rechazada';

export interface SolicitudParcialDto {
  id: string;
  numeroPedido: string;
  sucursalId: string;
  etiquetas: string[];
  esperadas: number;
  enPt: number;
  motivo: string;
  estado: EstadoSolicitudParcial;
  solicitadaPorId: string;
  solicitadaAt: string;
  nivel: 'credito' | 'ventas' | null;
  suplencia: string | null;
  motivoResolucion: string | null;
  resueltaPorId: string | null;
  resueltaAt: string | null;
}

export interface SolicitudesParcialesPagina {
  items: SolicitudParcialDto[];
  total: number;
}

export interface SolicitarParcialBody {
  numeroPedido: string;
  etiquetas: string[];
  motivo: string;
}

export interface ResolverParcialBody {
  autorizar: boolean;
  nivel?: 'credito' | 'ventas';
  suplencia?: string;
  motivo: string;
}

/** F1-PT-06 / M-351 / M-352: caso de daño, rechazo o devolución de piezas. */
export type EtapaCaso = 'AntesDeCarga' | 'RechazoEnSitio' | 'DevolucionCliente';
export type EstadoCaso = 'Abierto' | 'Dictaminado' | 'Cerrado';
export type DictamenCaso = 'RegresarDisponible' | 'Reproceso' | 'Refabricacion' | 'Desecho';

export interface CasoDanoDto {
  id: string;
  folio: string;
  numeroPedido: string;
  sucursalId: string;
  etapa: EtapaCaso;
  motivo: string;
  tieneEvidencia: boolean;
  estado: EstadoCaso;
  piezas: { etiqueta: string; posicion: number; estado: string }[];
  registradoPorId: string;
  registradoAt: string;
  dictamen: DictamenCaso | null;
  observacionDictamen: string | null;
  pedidoRefabricacionAw: string | null;
  dictaminadoPorId: string | null;
  dictaminadoAt: string | null;
  facturaFolio: string | null;
  correctivo: 'NotaCredito' | 'CancelacionFactura' | null;
  documento: { id: string; folio: string | null; estado: string; pendiente: boolean } | null;
  cerradoAt: string | null;
}

export interface CasosPagina {
  items: CasoDanoDto[];
  total: number;
}

export interface DictaminarCasoBody {
  dictamen: DictamenCaso;
  observacion: string;
  pedidoRefabricacionAw?: string;
}

export interface CancelarFacturaCasoBody {
  motivoSat: '01' | '02' | '03' | '04';
  uuidSustituto?: string;
}
