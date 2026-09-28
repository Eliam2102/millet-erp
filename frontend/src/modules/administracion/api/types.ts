/**
 * Tipos del API REST del módulo Administración — espejo de los DTOs
 * del backend en <c>Millet.Administracion.Application.*</c>
 * (F-Admin-PR2.3 / EmpresasEndpoints / DepartamentosEndpoints).
 */

/**
 * Estatus de un catálogo organizacional (sucursales, departamentos).
 * Mirror del enum <c>EstatusCatalogo</c> backend.
 */
export const EstatusCatalogo = {
  Activo: 0,
  Inactivo: 1,
  EnRevision: 2,
} as const;
export type EstatusCatalogo =
  (typeof EstatusCatalogo)[keyof typeof EstatusCatalogo];

export interface EmpresaResponse {
  id: string;
  rfc: string;
  razonSocial: string;
  nombreComercial: string | null;
  regimenFiscal: string;
  /** IVA default (fracción 0–1) para captura manual en Facturación (FAC-DET-PR2). */
  tasaIvaDefault: number | null;
  /** CP fiscal (LugarExpedicion del CFDI 4.0, F12-PR1). null = sin capturar. */
  codigoPostal: string | null;
  activa: boolean;
  version: number;
}

export interface ListarEmpresasResponse {
  items: EmpresaResponse[];
  total: number;
}

export const TipoSucursal = {
  Taller: 1,
  Planta: 2,
} as const;
export type TipoSucursal = (typeof TipoSucursal)[keyof typeof TipoSucursal];

export interface SucursalResponse {
  id: string;
  clave: string;
  nombre: string;
  tipo: TipoSucursal;
  /** Enum <c>EstatusCatalogo</c> serializado como número. */
  estatus: number;
  version: number;
  /**
   * Clave con la que A+W refiere esta sucursal en sus pedidos (ingesta
   * ADR-0048, p.ej. "CONKAL"). null = no recibe pedidos de A+W.
   */
  claveAw?: string | null;
  zonaHoraria?: string;
}

export interface DepartamentoResponse {
  id: string;
  clave: string;
  nombre: string;
  /** Enum <c>EstatusCatalogo</c> serializado como número. */
  estatus: number;
  version: number;
}

export interface EmpresaDetalleResponse {
  empresa: EmpresaResponse;
  sucursales: SucursalResponse[];
  departamentos: DepartamentoResponse[];
}

/**
 * Una asignación N:M Sucursal ↔ Departamento (PR-A1 backend).
 * Mirror de <c>SucursalDepartamentoResponse</c>. Trae los datos del
 * departamento (clave + nombre) ya joineados para evitar un fetch extra.
 */
export interface SucursalDepartamentoResponse {
  sucursalId: string;
  departamentoId: string;
  departamentoClave: string;
  departamentoNombre: string;
  /** <c>EstatusCatalogo</c> (Activo/Inactivo/EnRevision). */
  estatus: number;
  version: number;
}

export interface ListarDepartamentosDeSucursalResponse {
  items: SucursalDepartamentoResponse[];
  total: number;
}

/**
 * Una asignación N:M Sucursal ↔ Puesto ↔ Departamento (F1-ADM-01 Fase
 * 2/3 backend + Parte E "un puesto en varios departamentos"). Mirror
 * de <c>SucursalPuestoResponse</c>. Análogo exacto de
 * <see cref="SucursalDepartamentoResponse"/>, salvo que la unicidad
 * ahora es <c>(sucursalId, puestoId, departamentoId)</c>: un mismo
 * puesto puede tener una fila por cada departamento activo de la
 * sucursal en el que participa.
 */
export interface SucursalPuestoResponse {
  sucursalId: string;
  puestoId: string;
  puestoClave: string;
  puestoNombre: string;
  departamentoId: string;
  departamentoNombre?: string | null;
  /**
   * Rol sugerido propio de esta asignación (sucursal+puesto+departamento).
   * <c>null</c> = sin excepción, hereda el rol sugerido del puesto.
   */
  rolSugeridoId: string | null;
  /**
   * Rol sugerido efectivo a precargar en el wizard de alta:
   * <c>rolSugeridoId ?? rolSugeridoDelPuesto</c>. Siempre solo
   * sugerencia editable (01-04) — nunca se asigna en silencio.
   */
  rolSugeridoEfectivoId: string | null;
  /** <c>EstatusCatalogo</c> (Activo/Inactivo/EnRevision). */
  estatus: number;
  version: number;
}

export interface ListarPuestosDeSucursalResponse {
  items: SucursalPuestoResponse[];
  total: number;
}

/**
 * Una asignación N:M Usuario ↔ Sucursal (F1-ADM-01 Fase 2 backend,
 * módulo Identidad). Mirror de <c>UsuarioSucursalResponse</c>. Trae
 * email/nombre del usuario ya joineados, mismo criterio que
 * <see cref="SucursalDepartamentoResponse"/>.
 */
export interface UsuarioSucursalResponse {
  sucursalId: string;
  usuarioId: string;
  usuarioEmail: string;
  usuarioNombre: string;
  /** <c>EstatusCatalogo</c> (Activo/Inactivo/EnRevision). */
  estatus: number;
  version: number;
}

export interface ListarUsuariosPorSucursalResponse {
  items: UsuarioSucursalResponse[];
  total: number;
}

// ─── Canales de venta (FAC-ING-PR3) ────────────────────────────────

/**
 * Un canal de venta del catálogo <c>compartido.canales_venta</c>
 * (FAC-ING-PR2 backend). Mirror de <c>CanalVentaResponse</c>. El id es
 * un short asignado por la app (NO identity); inmutable una vez creado
 * porque ya está persistido en pedidos y facturas.
 */
export interface CanalVentaResponse {
  id: number;
  nombre: string;
  /** Enum <c>EstatusCatalogo</c> serializado como número. */
  estatus: number;
  version: number;
  /**
   * GRUPPE con el que A+W refiere este canal en la ingesta de pedidos
   * (ADR-0048). null = el canal no recibe pedidos de A+W.
   */
  claveAw: string | null;
}

// ─── Commands (request bodies) ─────────────────────────────────────

export interface CrearEmpresaCommand {
  /** <c>Guid.Empty</c> ⇒ backend autogenera. El frontend siempre lo
   * deja vacío; existe solo para tests deterministas. */
  id?: string;
  rfc: string;
  razonSocial: string;
  regimenFiscal: string;
  nombreComercial: string | null;
}

export interface ActualizarEmpresaPayload {
  razonSocial?: string | null;
  nombreComercial?: string | null;
  regimenFiscal?: string | null;
  limpiarNombreComercial?: boolean | null;
  tasaIvaDefault?: number | null;
  limpiarTasaIvaDefault?: boolean | null;
  codigoPostal?: string | null;
}

export interface CrearSucursalCommand {
  id?: string;
  clave: string;
  nombre: string;
  tipo?: TipoSucursal;
  claveAw?: string | null;
}

export interface ActualizarSucursalPayload {
  nombre?: string | null;
  tipo?: TipoSucursal | null;
  claveAw?: string | null;
  limpiarClaveAw?: boolean | null;
  zonaHoraria?: string | null;
}

export interface CrearCanalVentaCommand {
  nombre: string;
  claveAw?: string | null;
}

/**
 * Payload del PATCH /admin/canales-venta/{id}. Convención del repo:
 * null/ausente = no tocar; <c>limpiarClaveAw = true</c> desasocia el
 * canal de la ingesta A+W. <c>estatus</c> activa/desactiva sin tocar
 * el histórico (los documentos conservan el id).
 */
export interface ActualizarCanalVentaPayload {
  nombre?: string | null;
  claveAw?: string | null;
  limpiarClaveAw?: boolean | null;
  estatus?: EstatusCatalogo | null;
}

export interface CrearDepartamentoCommand {
  id?: string;
  clave: string;
  nombre: string;
}

export interface ActualizarDepartamentoPayload {
  nombre?: string | null;
}

// ─── Series y folios (F-Admin-PR6) ─────────────────────────────────

/**
 * Cuándo reinicia el contador de folios. Mirror de
 * <c>ReinicioPeriodo</c> backend (serializado como número por
 * convención del repo). En la UI <c>None</c> se etiqueta como
 * "Eterno" (no hay reinicio); el resto se muestra literal.
 */
export const ReinicioPeriodo = {
  None: 0,
  Anual: 1,
  Mensual: 2,
} as const;
export type ReinicioPeriodo =
  (typeof ReinicioPeriodo)[keyof typeof ReinicioPeriodo];

/**
 * Tipos de documento que pueden tener serie de folios. Mirror de
 * <c>TipoDocumentoSerie</c> backend.
 */
export const TipoDocumentoSerie = {
  OrdenCompra: 1,
  Cfdi: 2,
  NotaCredito: 3,
  Poliza: 4,
  FacturaAnticipo: 5,
} as const;
export type TipoDocumentoSerie =
  (typeof TipoDocumentoSerie)[keyof typeof TipoDocumentoSerie];

export interface SerieResponse {
  id: string;
  empresaId: string;
  sucursalId: string | null;
  tipoDocumento: TipoDocumentoSerie;
  prefijo: string;
  sufijo: string | null;
  reinicioPeriodo: ReinicioPeriodo;
  activa: boolean;
  version: number;
}

export interface SerieDetalleResponse {
  serie: SerieResponse;
  /** Ej. <c>"OC-2026-000123"</c>. El backend lo calcula contra el
   * período vigente; puede diferir del folio real si entre el GET y
   * el primer POST otra request consume folio. */
  proximoFolioPreview: string;
}

export interface ListarSeriesResponse {
  items: SerieResponse[];
  total: number;
}

export interface CrearSerieCommand {
  /** <c>Guid.Empty</c> ⇒ backend autogenera. */
  id?: string;
  empresaId: string;
  sucursalId: string | null;
  tipoDocumento: TipoDocumentoSerie;
  prefijo: string;
  sufijo: string | null;
  reinicioPeriodo: ReinicioPeriodo;
}

/**
 * Payload del PATCH /admin/series/{id}. Convención del repo:
 * <c>null</c>/ausente = no tocar; <c>limpiarSufijo = true</c> setea
 * Sufijo a null. Campos inmutables (EmpresaId, SucursalId,
 * TipoDocumento) NO se aceptan acá.
 */
export interface ActualizarSeriePayload {
  prefijo?: string | null;
  sufijo?: string | null;
  reinicioPeriodo?: ReinicioPeriodo | null;
  limpiarSufijo?: boolean | null;
}

// ─── Auditoría (F-Admin-PR7.2) ─────────────────────────────────────

/**
 * Una fila del log <c>core.audit_log</c> (ADR-0008) consolidada para
 * la UI. Mirror de <c>AuditLogEntryResponse</c> backend.
 *
 * <para><c>usuarioNombre</c> está siempre <c>null</c> hasta que el
 * backend prioritice el enriquecimiento cross-schema
 * (PLATFORM-TODO &lt;AuditUsuarioEnrich&gt;). Mientras tanto la UI
 * cae al <c>usuarioId</c> truncado.</para>
 */
export interface AuditLogEntryResponse {
  id: string;
  /** ISO 8601 con offset (DateTimeOffset C#). */
  timestamp: string;
  usuarioId: string | null;
  usuarioNombre: string | null;
  empresaId: string | null;
  sucursalId?: string | null;
  sucursalClave?: string | null;
  modulo: string;
  entidad: string;
  entidadId: string | null;
  operacion: string;
  /** JSON serializado del cambio. Forma típica: <c>{ before, after }</c>
   *  para updates, objeto plano para creates, etc. */
  cambios: string;
  correlationId: string;
}

export interface ConsultarBitacoraResponse {
  items: AuditLogEntryResponse[];
  total: number;
}

// ─── Parámetros globales (F-Admin-PR7.1) ───────────────────────────

/**
 * Tipo del valor de un parámetro global. Mirror de
 * <c>TipoParametro</c> backend (serializado como número por convención
 * del repo — el spec del PR menciona "Fecha" pero el real es "Json").
 */
export const TipoParametro = {
  Texto: 0,
  Numero: 1,
  Booleano: 2,
  Json: 3,
} as const;
export type TipoParametro =
  (typeof TipoParametro)[keyof typeof TipoParametro];

export interface ParametroResponse {
  id: string;
  clave: string;
  valor: string;
  tipo: TipoParametro;
  /** Módulo dueño del parámetro; <c>null</c> = parámetro global del
   *  sistema (no atado a un módulo). */
  modulo: string | null;
  descripcion: string;
  version: number;
}

export interface ListarParametrosResponse {
  items: ParametroResponse[];
}

/** Body del PATCH /admin/parametros/{clave}. El backend valida que el
 *  string parsee según el <c>tipo</c> declarado en la fila. */
export interface ActualizarParametroPayload {
  valor: string;
}

// ── Puestos y Empleados (ADM-FE-PR1, doc 10-catalogo-puestos-empleados) ──

/**
 * Mirror de <c>PuestoResponse</c> backend (F1-ADM-01.4). Con la Parte
 * E ("un puesto en varios departamentos de la sucursal"), el puesto
 * ya NO tiene un único departamento — eso ahora vive en la asignación
 * N:M por sucursal (<see cref="SucursalPuestoResponse"/>).
 * <c>departamentoId</c> aquí es solo "departamento de referencia"
 * (opcional, informativo del catálogo maestro); ninguna validación
 * lo usa como fuente de verdad.
 */
export interface PuestoResponse {
  id: string;
  clave: string;
  nombre: string;
  estatus: EstatusCatalogo;
  version: number;
  rolSugeridoId?: string | null;
  rolSugeridoNombre?: string | null;
  /** Departamento de referencia (opcional, informativo). */
  departamentoId?: string | null;
  departamentoNombre?: string | null;
}

/** Body del POST /admin/puestos. Id vacío ⇒ lo genera el backend. */
export interface CrearPuestoCommand {
  id: string;
  clave: string;
  nombre: string;
  rolSugeridoId?: string | null;
  /** Departamento de referencia (opcional, informativo). */
  departamentoId?: string | null;
}

/** Body del PATCH /admin/puestos/{id}. <c>null</c> = no tocar. */
export interface ActualizarPuestoPayload {
  nombre?: string | null;
  rolSugeridoId?: string | null;
  limpiarRolSugerido?: boolean;
  /** Departamento de referencia (opcional, informativo). */
  departamentoId?: string | null;
  limpiarDepartamento?: boolean;
}

/** Mirror de <c>EmpleadoResponse</c> backend. */
export interface EmpleadoResponse {
  id: string;
  empresaId: string;
  clave: string;
  nombre: string;
  email: string | null;
  puestoId: string | null;
  jefeDirectoId: string | null;
  sucursalId: string | null;
  departamentoId: string | null;
  usuarioId: string | null;
  codigoNomina: string | null;
  estatus: EstatusCatalogo;
  version: number;
}

/** Body del POST /admin/empleados. Id vacío ⇒ lo genera el backend. */
export interface CrearEmpleadoCommand {
  id: string;
  empresaId: string;
  clave?: string | null;
  nombre: string;
  email?: string | null;
  puestoId?: string | null;
  jefeDirectoId?: string | null;
  sucursalId?: string | null;
  departamentoId?: string | null;
  usuarioId?: string | null;
  codigoNomina?: string | null;
}

/**
 * Body del PATCH /admin/empleados/{id}. Convención backend: campo
 * <c>null</c>/ausente = no tocar; flags <c>limpiar*</c> ponen el campo
 * nullable en null. Inmutables: clave y empresaId.
 */
export interface ActualizarEmpleadoPayload {
  nombre?: string | null;
  email?: string | null;
  limpiarEmail?: boolean;
  puestoId?: string | null;
  limpiarPuesto?: boolean;
  jefeDirectoId?: string | null;
  limpiarJefeDirecto?: boolean;
  sucursalId?: string | null;
  limpiarSucursal?: boolean;
  departamentoId?: string | null;
  limpiarDepartamento?: boolean;
  usuarioId?: string | null;
  limpiarUsuario?: boolean;
  codigoNomina?: string | null;
  limpiarCodigoNomina?: boolean;
  emailContacto?: string | null;
  limpiarEmailContacto?: boolean;
}
