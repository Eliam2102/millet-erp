/**
 * Tipos compartidos del módulo Almacén (FE-F1-PR1+). Mirror manual
 * de los DTOs que devuelve el backend en
 * <c>/api/v1/almacen/...</c>. Cuando ADR-0017 (codegen TS desde
 * OpenAPI) entre, este archivo se vuelve autogenerado.
 *
 * <para>Convención: enums se modelan como <c>const</c> + <c>type</c>
 * union para cumplir <c>erasableSyntaxOnly</c> (mismo patrón que
 * <c>compras/api/types.ts</c>).</para>
 */

/** Estatus catálogo (alineado a <c>Catalogos.Domain.EstatusCatalogo</c>). */
export const EstatusCatalogo = {
  Activo: 0,
  Inactivo: 1,
  Borrador: 2,
} as const satisfies Record<string, number>;
export type EstatusCatalogo =
  (typeof EstatusCatalogo)[keyof typeof EstatusCatalogo];

/** Tipo de sub-almacén (alineado al backend §5.1 + A15). */
export const TipoSubAlmacen = {
  Insumos: 0,
  MaterialesDirectos: 1,
  MaterialEnRevision: 2,
  Transitorio: 3,
} as const satisfies Record<string, number>;
export type TipoSubAlmacen =
  (typeof TipoSubAlmacen)[keyof typeof TipoSubAlmacen];

export interface AlmacenListItem {
  id: string;
  clave: string;
  nombre: string;
  sucursalId: string;
  estatus: EstatusCatalogo;
}

export interface SubAlmacenListItem {
  id: string;
  almacenId: string;
  clave: string;
  nombre: string;
  tipo: TipoSubAlmacen;
  estatus: EstatusCatalogo;
}

export interface AlmacenDetalle {
  id: string;
  clave: string;
  nombre: string;
  sucursalId: string;
  estatus: EstatusCatalogo;
  version: number;
  subAlmacenes: SubAlmacenListItem[];
}

export interface PagedResponse<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

/** Etiquetas amigables para la UI (no van al backend). */
export const TipoSubAlmacenLabels: Record<TipoSubAlmacen, string> = {
  [TipoSubAlmacen.Insumos]: 'Insumos',
  [TipoSubAlmacen.MaterialesDirectos]: 'Materiales directos',
  [TipoSubAlmacen.MaterialEnRevision]: 'Material en revisión',
  [TipoSubAlmacen.Transitorio]: 'Transitorio',
};

export const EstatusCatalogoLabels: Record<EstatusCatalogo, string> = {
  [EstatusCatalogo.Activo]: 'Activo',
  [EstatusCatalogo.Inactivo]: 'Inactivo',
  [EstatusCatalogo.Borrador]: 'Borrador',
};

// ─── Reabasto / reorden N1-N2 (ADR-0047 PR5.A/5.F) ──────────────────────────
// El código usa "reorden" (alineado al backend `almacen.reorden.*`); la UI lo
// muestra como "Reabasto" (término de negocio de Millet). Enums como int, sin
// JsonStringEnumConverter global (mismo criterio que el resto del ERP).

/** Nivel al que se configura el reabasto
 * (mirror de <c>Almacen.Domain.Catalogo.NivelReorden</c>). */
export const NivelReorden = {
  Sucursal: 0,
  Almacen: 1,
} as const satisfies Record<string, number>;
export type NivelReorden = (typeof NivelReorden)[keyof typeof NivelReorden];

/** Objetivo de reposición: cuál de los tres valores dispara el faltante
 * (mirror de <c>Almacen.Domain.Catalogo.ObjetivoReposicion</c>). */
export const ObjetivoReposicion = {
  Minimo: 0,
  Maximo: 1,
  Reorden: 2,
} as const satisfies Record<string, number>;
export type ObjetivoReposicion =
  (typeof ObjetivoReposicion)[keyof typeof ObjetivoReposicion];

/** Fila de la bandeja de reabasto — mirror de <c>ConfiguracionReordenListItem</c>.
 * El nombre de la entidad (sucursal/almacén) NO viene del backend: lo resuelve
 * el FE según <c>nivel</c> (grilla polimórfica). */
export interface ConfiguracionReordenListItem {
  id: string;
  articuloId: string;
  nivel: NivelReorden;
  entidadId: string;
  minimo: number;
  maximo: number;
  puntoReorden: number;
  autoRequisicion: boolean;
  objetivo: ObjetivoReposicion;
  estatus: EstatusCatalogo;
  articuloClave: string | null;
  articuloDescripcion: string | null;
}

/** Respuesta de crear/editar/desactivar — mirror de <c>ConfiguracionReordenResponse</c>. */
export interface ConfiguracionReordenResponse {
  id: string;
  articuloId: string;
  nivel: NivelReorden;
  entidadId: string;
  minimo: number;
  maximo: number;
  puntoReorden: number;
  autoRequisicion: boolean;
  objetivo: ObjetivoReposicion;
  estatus: EstatusCatalogo;
}

/** Cuerpo de <c>POST /api/v1/almacen/reorden</c>
 * (mirror de <c>CrearConfiguracionReordenCommand</c>). */
export interface CrearConfiguracionReordenPayload {
  articuloId: string;
  nivel: NivelReorden;
  entidadId: string;
  minimo: number;
  maximo: number;
  puntoReorden: number;
  autoRequisicion: boolean;
  objetivo: ObjetivoReposicion;
}

/** Cuerpo de <c>PATCH /api/v1/almacen/reorden/{id}</c>
 * (mirror de <c>EditarConfiguracionReordenCommand</c>): la llave
 * artículo/nivel/entidad es inmutable, solo se edita la política. */
export interface EditarConfiguracionReordenPayload {
  id: string;
  minimo: number;
  maximo: number;
  puntoReorden: number;
  autoRequisicion: boolean;
  objetivo: ObjetivoReposicion;
}

/** Etiquetas UI (no van al backend). Términos de negocio en español. */
export const NivelReordenLabels: Record<NivelReorden, string> = {
  [NivelReorden.Sucursal]: 'Sucursal',
  [NivelReorden.Almacen]: 'Almacén',
};

export const ObjetivoReposicionLabels: Record<ObjetivoReposicion, string> = {
  [ObjetivoReposicion.Minimo]: 'Mínimo',
  [ObjetivoReposicion.Maximo]: 'Máximo',
  [ObjetivoReposicion.Reorden]: 'Punto de reorden',
};

// ─── Ubicación de artículos (asignación N4, ADR-0047 PR3/PR C) ──────────────
// La pantalla se llama "Ubicación de artículos"; el código/endpoints/permiso
// usan "asignaciones"/"ubicaciones" (técnico).

/** Ubicación N4 — mirror de <c>Almacen.Application.Catalogo.UbicacionListItem</c>.
 * Enriquecida con clave/nombre del sub-almacén (N3) y almacén (N2) padres (join
 * in-context, INNER → los 4 campos de padre son no-null). Puebla el
 * <c>UbicacionSelector</c>; el padre distingue las "ÚNICA" (misma clave). */
export interface UbicacionListItem {
  id: string;
  subAlmacenId: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
  esDefault: boolean;
  subAlmacenClave: string;
  subAlmacenNombre: string;
  almacenClave: string;
  almacenNombre: string;
}

/** Cuerpo de <c>POST /api/v1/almacen/ubicaciones</c> — mirror de
 * <c>CrearUbicacionCommand</c>. */
export interface CrearUbicacionPayload {
  subAlmacenId: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
}

/** Respuesta de crear — mirror de <c>CrearUbicacionResponse</c>. */
export interface CrearUbicacionResponse {
  id: string;
  subAlmacenId: string;
  clave: string;
}

/** Cuerpo de <c>PATCH /api/v1/almacen/ubicaciones/{id}</c> — mirror de
 * <c>EditarUbicacionCommand</c>: solo clave/nombre (el sub-almacén no se muda y
 * el estatus va por desactivar/reactivar). */
export interface EditarUbicacionPayload {
  id: string;
  clave: string;
  nombre: string;
}

/** Respuesta de desactivar/reactivar — mirror de <c>UbicacionEstatusResponse</c>. */
export interface UbicacionEstatusResponse {
  id: string;
  estatus: EstatusCatalogo;
}

/** Fila de "Ubicación de artículos" — mirror de <c>AsignacionListItem</c> (tras
 * PR C, sin min/máx/reorden: pura relación artículo↔ubicación). El nombre de la
 * ubicación NO viene aquí: el FE lo resuelve con <c>useUbicaciones</c> + mapById. */
export interface AsignacionListItem {
  id: string;
  ubicacionId: string;
  articuloId: string;
  estatus: EstatusCatalogo;
  articuloClave: string | null;
  articuloDescripcion: string | null;
}

/** Respuesta de asignar/desasignar — mirror de <c>AsignacionResponse</c>. */
export interface AsignacionResponse {
  id: string;
  ubicacionId: string;
  articuloId: string;
  estatus: EstatusCatalogo;
}

/** Cuerpo de <c>POST /api/v1/almacen/asignaciones</c> — mirror de
 * <c>AsignarArticuloAUbicacionCommand</c> (tras PR C: solo la llave). */
export interface AsignarArticuloAUbicacionPayload {
  ubicacionId: string;
  articuloId: string;
}

// ─── Recepciones (FE-F2-PR1) ───────────────────────────────────────────────

/**
 * Estado del movimiento de inventario
 * (mirror de <c>Almacen.Domain.Movimientos.EstadoMovimiento</c>).
 */
export const EstadoMovimiento = {
  Borrador: 0,
  Validado: 1,
  Registrado: 2,
  Cancelado: 3,
} as const satisfies Record<string, number>;
export type EstadoMovimiento =
  (typeof EstadoMovimiento)[keyof typeof EstadoMovimiento];

export const EstadoMovimientoLabels: Record<EstadoMovimiento, string> = {
  [EstadoMovimiento.Borrador]: 'Borrador',
  [EstadoMovimiento.Validado]: 'Validado',
  [EstadoMovimiento.Registrado]: 'Registrado',
  [EstadoMovimiento.Cancelado]: 'Cancelado',
};

export interface RecepcionListItem {
  id: string;
  folio: string;
  fechaMovimiento: string; // DateOnly serialized YYYY-MM-DD
  subAlmacenId: string;
  ordenCompraId: string | null;
  // Folio de la OC resuelto en backend (ADR-0042). null = no resolvió → el
  // front cae al id (truncado).
  ordenCompraFolio: string | null;
  cfdiRecibidoId: string | null;
  montoTotalMxn: number;
  estado: EstadoMovimiento;
}

export interface RecepcionLineaItem {
  id: string;
  posicion: number;
  articuloId: string;
  // Resueltos en backend (ADR-0042). null = no resolvió → el front cae al id.
  articuloClave: string | null;
  articuloDescripcion: string | null;
  cantidad: number;
  unidadMedida: string;
  costoUnitarioMxn: number;
  montoTotalMxn: number;
}

export interface RecepcionDetalle {
  id: string;
  folio: string;
  fechaMovimiento: string;
  subAlmacenId: string;
  // Nombres resueltos en backend (ADR-0042). null = no resolvió → el front
  // cae al id/clave cruda.
  subAlmacenClave: string | null;
  subAlmacenNombre: string | null;
  ordenCompraId: string | null;
  ordenCompraFolio: string | null;
  cfdiRecibidoId: string | null;
  facturaId: string | null;
  estado: EstadoMovimiento;
  version: number;
  observaciones: string | null;
  registradoAt: string | null; // ISO 8601 timestamp
  registradoPor: string | null;
  lineas: RecepcionLineaItem[];
  /** UUID fiscal del SAT del CFDI referenciado (ADR-0042); null → id truncado. */
  cfdiUuidFiscal: string | null;
  /** Folio del proveedor de la factura CxP (ADR-0042); null → id truncado. */
  facturaFolio: string | null;
}

/**
 * Variante A — recepción con factura/CFDI ya conocido (insumos,
 * refacciones). Costo de OC.
 */
export interface RegistrarRecepcionConFacturaCommand {
  ordenCompraId: string;
  fechaMovimiento: string; // YYYY-MM-DD
  /**
   * Vínculo fiscal obligatorio (§5.4): CFDI del repositorio de CxP, o
   * el folio fiscal (UUID SAT) capturado del impreso cuando el XML aún
   * no llegó — al menos uno de los dos.
   */
  cfdiRecibidoId: string | null;
  cfdiUuidFiscal: string | null;
  observaciones: string | null;
  /**
   * PR4: helper de cabecera (bin N4) que el almacenista auto-aplicó a las
   * líneas sin ubicación. null = no usó el helper. Solo reportería — la
   * ubicación efectiva de cada línea va en <c>lineas[].ubicacionId</c>.
   */
  ubicacionHelperId: string | null;
  lineas: RegistrarRecepcionLineaInput[];
}

/**
 * Variante B — recepción con packing list, factura pendiente
 * (materiales directos no-vidrio: interlayer, silicones, pinturas).
 * Costo de OC. Marca <c>factura_pendiente=true</c>.
 */
export interface RegistrarRecepcionConPackingListCommand {
  ordenCompraId: string;
  fechaMovimiento: string;
  packingListBlobRef: string;
  observaciones: string | null;
  /** PR4: helper de cabecera (ver variante A). null = no lo usó. */
  ubicacionHelperId: string | null;
  lineas: RegistrarRecepcionLineaInput[];
}

export interface RegistrarRecepcionLineaInput {
  articuloId: string;
  lineaOcId: string | null;
  cantidad: number;
  ubicacionReferencia: string | null;
  // C7.2b: bin real destino (rack N4). Obligatorio en entradas.
  ubicacionId: string | null;
  comentario: string | null;
}

export interface RegistrarRecepcionResponse {
  recepcionId: string;
  folio: string;
}

// ─── Salidas (FE-F3-PR1) ────────────────────────────────────────────────────

export interface SalidaListItem {
  id: string;
  folio: string;
  fechaMovimiento: string;
  subAlmacenId: string;
  rqId: string | null;
  esPorVale: boolean;
  personaDestinatariaId: string | null;
  montoTotalMxn: number;
  estado: EstadoMovimiento;
  /**
   * RQ vinculada posteriormente para regularizar el vale (A14).
   * Null si el vale aún no se regulariza, o si la salida no es por vale.
   */
  rqRegularizadoraId: string | null;
  /**
   * Folio legible de la RQ surtida (ADR-0042). Null sin RQ directa
   * (vales) o si el puerto no resolvió — la bandeja muestra "—",
   * nunca el GUID.
   */
  rqFolio: string | null;
  /**
   * Folio de la RQ regularizadora de un vale (A14). La bandeja lo
   * muestra como "Vale · {folio}". Null si el vale no se ha
   * regularizado.
   */
  rqRegularizadoraFolio: string | null;
}

export interface SalidaLineaItem {
  id: string;
  posicion: number;
  articuloId: string;
  /** Resueltos en backend (ADR-0042). Null = el read-port no resolvió. */
  articuloClave: string | null;
  articuloDescripcion: string | null;
  cantidad: number;
  unidadMedida: string;
  costoUnitarioMxn: number;
  montoTotalMxn: number;
  centroCostoId: string | null;
  /** Resueltos en backend vía IDim3ReadPort (sin filtro, incluye inactivas —
   * ADR-0050 §3). Null = irresoluble → el FE cae a "No catalogado". */
  centroCostoClave: string | null;
  centroCostoNombre: string | null;
  proyectoId: string | null;
}

export interface SalidaDetalle {
  id: string;
  folio: string;
  fechaMovimiento: string;
  subAlmacenId: string;
  /** Nombres resueltos en backend (ADR-0042). Null = cae al id/clave cruda. */
  subAlmacenClave: string | null;
  subAlmacenNombre: string | null;
  rqId: string | null;
  rqFolio: string | null;
  esPorVale: boolean;
  valeBlobRef: string | null;
  personaDestinatariaId: string | null;
  personaDestinatariaNombre: string | null;
  estado: EstadoMovimiento;
  version: number;
  observaciones: string | null;
  registradoAt: string | null;
  registradoPor: string | null;
  /**
   * RQ vinculada posteriormente para regularizar el vale (A14).
   * Null si el vale aún no se regulariza, o si la salida es Variante A.
   */
  rqRegularizadoraId: string | null;
  rqRegularizadoraFolio: string | null;
  lineas: SalidaLineaItem[];
}

/**
 * Variante A — salida normal con RQ. Consume reserva si existe (A19).
 * Costo de la reserva (o CPP si no hay reserva).
 */
export interface RegistrarSalidaConRequisicionCommand {
  requisicionId: string;
  // Salida-por-línea C2: sin subAlmacenId — el backend lo deriva del bin de
  // cada línea (obligatorio). El vale (variante B) sí lo conserva.
  fechaMovimiento: string;
  personaDestinatariaId: string | null;
  observaciones: string | null;
  lineas: RegistrarSalidaLineaInput[];
}

/**
 * Variante B — vale urgente sin RQ (A14). Adjunta vale escaneado/firmado
 * y se regulariza en 48h vinculando una RQ aprobada.
 */
export interface RegistrarSalidaPorValeCommand {
  // Salida-por-línea (vale): sin subAlmacenId — el backend lo deriva del bin de
  // cada línea (obligatorio), igual que la salida-con-RQ.
  fechaMovimiento: string;
  valeBlobRef: string;
  personaDestinatariaId: string | null;
  observaciones: string | null;
  lineas: RegistrarSalidaLineaInput[];
}

export interface RegistrarSalidaLineaInput {
  articuloId: string;
  lineaRqId: string | null;
  cantidad: number;
  centroCostoId: string | null;
  proyectoId: string | null;
  ubicacionReferencia: string | null;
  // C7.2b: bin real de donde sale (o la ÚNICA). Opcional en el backend
  // (NULL → ÚNICA); el FE siempre lo manda.
  ubicacionId: string | null;
  comentario: string | null;
}

export interface RegistrarSalidaResponse {
  salidaId: string;
  folio: string;
}

/** Regularización del vale: vincula una RQ aprobada posterior. */
export interface RegularizarValeCommand {
  salidaId: string;
  rqRegularizadoraId: string;
}

// ─── Devoluciones internas 8.A + MAT-REV (FE-F4-PR1) ────────────────────────

/**
 * Aplicar devolución interna: una cantidad de una salida origen
 * vuelve al sub-almacén MAT-REV (o al sub-almacén destino) con el
 * costo de la salida.
 */
export interface AplicarDevolucionInternaCommand {
  salidaOrigenId: string;
  subAlmacenDestinoId: string;
  fechaMovimiento: string; // YYYY-MM-DD
  estadoMaterial: string;
  motivo: string;
  observaciones: string | null;
  lineas: DevolucionInternaLineaInput[];
}

export interface DevolucionInternaLineaInput {
  lineaSalidaOrigenId: string;
  cantidadADevolver: number;
  // C7.2b: bin real destino (entrada, obligatorio).
  ubicacionId: string;
}

export interface AplicarDevolucionInternaResponse {
  devolucionId: string;
  folio: string;
}

/**
 * Decisión Calidad: dar de baja material en MAT-REV (destrucción).
 */
export interface BajaPorDanoCommand {
  subAlmacenMatRevId: string;
  fechaMovimiento: string;
  motivo: string;
  lineas: MatRevLineaInput[];
}

/**
 * Decisión Calidad: reincorporar material en MAT-REV al inventario
 * activo.
 */
export interface ReincorporacionTrasRevisionCommand {
  subAlmacenDestinoId: string;
  fechaMovimiento: string;
  motivo: string;
  lineas: MatRevLineaInput[];
}

export interface MatRevLineaInput {
  articuloId: string;
  cantidad: number;
  // C7.2b: bin real. Requerido en reincorporación (entrada); libre en baja.
  ubicacionId?: string | null;
}

export interface BajaPorDanoResponse {
  movimientoId: string;
  folio: string;
}

// ─── Devoluciones a proveedor 8.B (FE-F4-PR1) ───────────────────────────────

/**
 * Estado del flujo de devolución a proveedor (mirror del enum
 * <c>Almacen.Domain.DevolucionesProveedor.EstadoDevolucionProveedor</c>).
 */
export const EstadoDevolucionProveedor = {
  Borrador: 0,
  EnAutorizacion: 1,
  Autorizada: 2,
  Registrada: 3,
  ConciliadaConNcFiscal: 4,
  Rechazada: 5,
} as const satisfies Record<string, number>;
export type EstadoDevolucionProveedor =
  (typeof EstadoDevolucionProveedor)[keyof typeof EstadoDevolucionProveedor];

export const EstadoDevolucionProveedorLabels: Record<
  EstadoDevolucionProveedor,
  string
> = {
  [EstadoDevolucionProveedor.Borrador]: 'Borrador',
  [EstadoDevolucionProveedor.EnAutorizacion]: 'En autorización',
  [EstadoDevolucionProveedor.Autorizada]: 'Autorizada',
  [EstadoDevolucionProveedor.Registrada]: 'Registrada',
  [EstadoDevolucionProveedor.ConciliadaConNcFiscal]: 'Conciliada NC fiscal',
  [EstadoDevolucionProveedor.Rechazada]: 'Rechazada',
};

export interface DevolucionProveedorListItem {
  id: string;
  proveedorId: string;
  estado: EstadoDevolucionProveedor;
  montoTotalMxn: number;
  solicitadaAt: string; // ISO 8601
  autorizadaAt: string | null;
  registradaAt: string | null;
  conciliadaConNcFiscalAt: string | null;
  folioMovimientoSalida: string | null;
  /** Razón social resuelta en backend (ADR-0042); null → id truncado. */
  proveedorNombre: string | null;
}

export interface DevolucionProveedorLineaItem {
  id: string;
  posicion: number;
  articuloId: string;
  cantidad: number;
  unidadMedida: string;
  costoUnitarioMxn: number;
  montoTotalMxn: number;
  lineaRecepcionOrigenId: string | null;
  /** Etiqueta resuelta en backend (ADR-0042); null → id truncado. */
  articuloClave: string | null;
  articuloDescripcion: string | null;
}

export interface DevolucionProveedorEvidenciaItem {
  id: string;
  tipoEvidencia: string;
  nombreArchivo: string;
  blobRef: string;
  comentario: string | null;
}

export interface DevolucionProveedorDetalle {
  id: string;
  proveedorId: string;
  recepcionOrigenId: string | null;
  facturaProveedorOrigenId: string | null;
  ordenCompraOrigenId: string | null;
  subAlmacenOrigenId: string | null;
  estado: EstadoDevolucionProveedor;
  motivo: string;
  solicitadaPor: string;
  solicitadaAt: string;
  autorizadaPor: string | null;
  autorizadaAt: string | null;
  motivoRechazo: string | null;
  rechazadaAt: string | null;
  movimientoSalidaId: string | null;
  folioMovimientoSalida: string | null;
  registradaAt: string | null;
  notaCreditoFiscalId: string | null;
  conciliadaConNcFiscalAt: string | null;
  version: number;
  lineas: DevolucionProveedorLineaItem[];
  evidencias: DevolucionProveedorEvidenciaItem[];
  /** Razón social resuelta en backend (ADR-0042); null → id truncado. */
  proveedorNombre: string | null;
  /** Folio de la NC fiscal que concilió (ADR-0042); null → id truncado. */
  notaCreditoFolio: string | null;
}

export interface IniciarDevolucionAProveedorCommand {
  proveedorId: string;
  motivo: string;
  recepcionOrigenId: string | null;
  facturaProveedorOrigenId: string | null;
  ordenCompraOrigenId: string | null;
  subAlmacenOrigenId: string | null;
  lineas: DevolucionProveedorLineaInput[];
}

export interface DevolucionProveedorLineaInput {
  articuloId: string;
  cantidad: number;
  unidadMedida: string;
  costoUnitarioMxn: number;
  lineaRecepcionOrigenId: string | null;
}

export interface IniciarDevolucionAProveedorResponse {
  devolucionId: string;
}

export interface AdjuntarEvidenciaDevolucionAProveedorCommand {
  devolucionId: string;
  tipoEvidencia: string;
  nombreArchivo: string;
  blobRef: string;
  comentario: string | null;
}

export interface DevolucionProveedorSalidaLineaBin {
  lineaDevolucionId: string;
  ubicacionId: string;
}

export interface RegistrarSalidaDevolucionAProveedorCommand {
  devolucionId: string;
  subAlmacenId: string;
  fechaMovimiento: string;
  // C7.2b: bin real por línea, capturado al registrar. Correlaciona por
  // lineaDevolucionId.
  bins?: DevolucionProveedorSalidaLineaBin[];
}

export interface RegistrarSalidaDevolucionAProveedorResponse {
  movimientoSalidaId: string;
  folioMovimientoSalida: string;
}

// ─── Conteos / Inventario físico (FE-F5-PR1) ────────────────────────────────

/**
 * Estado del conteo (mirror de
 * <c>Almacen.Domain.Conteos.EstadoConteo</c>).
 */
export const EstadoConteo = {
  Planificado: 0,
  EnCurso: 1,
  EnConciliacion: 2,
  Aprobado: 3,
  Aplicado: 4,
  Rechazado: 5,
} as const satisfies Record<string, number>;
export type EstadoConteo = (typeof EstadoConteo)[keyof typeof EstadoConteo];

export const EstadoConteoLabels: Record<EstadoConteo, string> = {
  [EstadoConteo.Planificado]: 'Planificado',
  [EstadoConteo.EnCurso]: 'En curso',
  [EstadoConteo.EnConciliacion]: 'En conciliación',
  [EstadoConteo.Aprobado]: 'Aprobado',
  [EstadoConteo.Aplicado]: 'Aplicado',
  [EstadoConteo.Rechazado]: 'Rechazado',
};

/**
 * Tipo de conteo. <b>Anual</b> bloquea salidas durante la captura
 * (A18); <b>Rotativo</b> no bloquea.
 */
export const TipoConteo = {
  Rotativo: 0,
  Anual: 1,
} as const satisfies Record<string, number>;
export type TipoConteo = (typeof TipoConteo)[keyof typeof TipoConteo];

export const TipoConteoLabels: Record<TipoConteo, string> = {
  [TipoConteo.Rotativo]: 'Rotativo',
  [TipoConteo.Anual]: 'Anual',
};

export interface ConteoListItem {
  id: string;
  tipo: TipoConteo;
  estado: EstadoConteo;
  fechaPlanificada: string; // YYYY-MM-DD
  subAlmacenId: string | null;
  filtroFamilia: string | null;
  fechaInicio: string | null; // ISO 8601
  fechaCierre: string | null;
  cantidadLineas: number;
  cantidadCapturadas: number;
}

export interface ConteoDetalle {
  id: string;
  tipo: TipoConteo;
  estado: EstadoConteo;
  fechaPlanificada: string;
  subAlmacenId: string | null;
  filtroFamilia: string | null;
  responsableId: string;
  fechaInicio: string | null;
  snapshotCapturadoAt: string | null;
  aprobadorId: string | null;
  fechaAprobacion: string | null;
  version: number;
  cantidadLineas: number;
  cantidadCapturadas: number;
  // Etiquetas legibles (ADR-0042); null → el FE cae al id truncado.
  subAlmacenClave: string | null;
  responsableNombre: string | null;
  aprobadorNombre: string | null;
}

/**
 * Línea de conteo para captura — sin <c>cantidadTeorica</c> (A6,
 * captura sin sesgo).
 */
export interface LineaConteoParaCapturarDto {
  id: string;
  articuloId: string;
  subAlmacenId: string;
  // C7.2c: rack contado. La clave viene enriquecida del backend (nunca GUID).
  ubicacionId: string;
  ubicacionClave: string;
  cantidadRealCapturada: number | null;
  requiereRecuento: boolean;
  // Artículo y sub-almacén legibles (ADR-0042); null → id truncado.
  articuloClave: string | null;
  articuloDescripcion: string | null;
  subAlmacenClave: string | null;
}

/**
 * Línea para la comparación (aprobador) — sí incluye teórico,
 * variación y valor. Endpoint distinto al de captura.
 */
export interface LineaConteoComparacionDto {
  id: string;
  articuloId: string;
  subAlmacenId: string;
  // C7.2c: rack contado (clave enriquecida).
  ubicacionId: string;
  ubicacionClave: string;
  cantidadTeorica: number;
  costoPromedioSnapshot: number;
  cantidadRealCapturada: number | null;
  variacionAbsoluta: number | null;
  variacionPorcentaje: number | null;
  variacionValorMxn: number | null;
  requiereRecuento: boolean;
  aprobadoIndividualmente: boolean;
  justificacion: string | null;
  // Artículo y sub-almacén legibles, mismo enriquecimiento que la captura.
  articuloClave: string | null;
  articuloDescripcion: string | null;
  subAlmacenClave: string | null;
}

export interface CrearConteoCommand {
  tipo: TipoConteo;
  fechaPlanificada: string;
  responsableId: string;
  subAlmacenId: string | null;
  filtroFamilia: string | null;
}

export interface CrearConteoResponse {
  conteoId: string;
}

// ─── Aprobación + aplicación (FE-F5-PR2) ────────────────────────────────────

export interface AgregarRecuentoCommand {
  conteoId: string;
  lineaId: string;
  cantidadRecontada: number;
}

export interface AgregarRecuentoResponse {
  recuentoId: string;
  secuencia: number;
}

export interface AprobarLineaIndividualmenteCommand {
  conteoId: string;
  lineaId: string;
  justificacion: string;
}

export interface RechazarConteoCommand {
  conteoId: string;
  motivo: string;
}

export interface AplicarConteoResponse {
  movimientosGenerados: number;
  montoNetoMxn: number;
}

export interface EvaluarVariacionesResponse {
  lineasMarcadasParaRecuento: number;
}

// ─── Saldos (FE-F6-PR1) ─────────────────────────────────────────────────────

export interface SaldoListItem {
  subAlmacenId: string;
  articuloId: string;
  cantidad: number;
  cantidadDisponible: number;
  costoPromedioMxn: number;
  valorInventarioMxn: number;
  // Resueltos en backend (ADR-0042). null = no resolvió → el front cae al id.
  articuloClave: string | null;
  articuloDescripcion: string | null;
}

/** Mirror de <c>SaldoUbicacionItem</c> (C7.2b) — una ubicación con existencia
 * (>0) de un artículo en un sub-almacén, incluida la ÚNICA (esDefault). Puebla
 * el selector de bin en salidas. Salida-por-línea C1: enriquecida con clave del
 * sub-almacén (N3) y almacén (N2) padres (join in-context INNER → no-null) para
 * la ruta "ALM › SUB · UBI" en el selector. N1 (sucursal) diferido. */
export interface SaldoUbicacionItem {
  ubicacionId: string;
  clave: string;
  nombre: string;
  subAlmacenClave: string;
  almacenClave: string;
  esDefault: boolean;
  cantidad: number;
  cantidadDisponible: number;
  costoPromedioMxn: number;
}

// ─── Consulta jerárquica (PR6, ADR-0047) ────────────────────────────────────

/** Tipo del nodo HIJO que devuelve la consulta jerárquica. */
export type TipoNodoJerarquia =
  | 'sucursal'
  | 'almacen'
  | 'subAlmacen'
  | 'ubicacion'
  | 'articulo';

/** Nivel del nodo QUE SE EXPANDE (query param `nodoTipo` del endpoint). */
export type NivelNodoJerarquia =
  | 'raiz'
  | 'sucursal'
  | 'almacen'
  | 'subAlmacen'
  | 'ubicacion';

/**
 * Mirror de <c>NodoJerarquiaDto</c> (PR6): un hijo inmediato del nodo
 * expandido con su subtotal (rollup de cantidad + valor; el CPP no se
 * promedia). <c>esDefault</c> solo significa algo en ubicaciones (badge
 * ÚNICA); <c>esHoja</c> lo fija el backend — sin chevron ni expansión.
 */
export interface NodoJerarquiaSaldo {
  tipo: TipoNodoJerarquia;
  id: string;
  clave: string;
  nombre: string;
  cantidad: number;
  valorInventarioMxn: number;
  esDefault: boolean;
  esHoja: boolean;
}

// ─── Cierre de mes (FE-F6-PR1) ──────────────────────────────────────────────

export interface EjecutarCierreMensualCommand {
  anio: number;
  mes: number;
}

export interface EjecutarCierreMensualResponse {
  cierreId: string;
  anio: number;
  mes: number;
  cerradoAt: string;
}

// ─── Reportes operativos (FE-F6-PR1, ADR-0036) ──────────────────────────────

/**
 * Shape canónico de un reporte (ADR-0036). El backend devuelve este
 * shape; el FE renderiza con <c>&lt;ReporteShell&gt;</c> + exporta
 * client-side a PDF/Excel.
 */
export interface ReporteResponse<TFila> {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: ColumnaDescriptor[];
  filas: TFila[];
  totales?: Record<string, number> | null;
}

export interface ColumnaDescriptor {
  clave: string;
  etiqueta: string;
  tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
  alineacion?: string | null;
  anchoPx?: number | null;
}

export interface AlfakHistorialFila {
  subAlmacenId: string;
  articuloId: string;
  saldoInicial: number;
  entradas: number;
  salidas: number;
  ajustesPositivos: number;
  ajustesNegativos: number;
  saldoFinal: number;
  costoPromedioFinal: number;
  valorInventarioFinal: number;
}

export interface ExistenciaMpCnkFila {
  subAlmacenId: string;
  articuloId: string;
  cantidad: number;
  cantidadDisponible: number;
  costoPromedioMxn: number;
  valorInventarioMxn: number;
}
