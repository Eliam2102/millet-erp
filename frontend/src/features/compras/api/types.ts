/**
 * DTOs y enums del módulo Compras Requisiciones (mirror manual del
 * backend). Mientras no exista codegen ADR-0017 desde OpenAPI, este
 * archivo es la fuente de verdad del shape de datos que la UI consume
 * y se mantiene en sincronía a mano.
 *
 * <para>El backend serializa enums como **número** (no usa
 * <c>JsonStringEnumConverter</c>): los integration tests verifican
 * <c>body.GetProperty("estado").GetInt32() == 0</c>, etc. Por eso los
 * declaramos como objetos <c>as const</c> con valores numéricos —
 * evita un parser intermedio y mantiene tipos estrictos. Helpers
 * <c>estadoToString</c> / <c>naturalezaToString</c> resuelven el label
 * humano cuando se necesita.</para>
 *
 * <para>Si el backend cambia a serialización por string en el futuro,
 * este archivo y los hooks consumidores son los únicos puntos a
 * actualizar. Test <c>permission-codes.test.ts</c> hace lo equivalente
 * para permisos; podría replicarse aquí si la divergencia se vuelve
 * problema.</para>
 */

// ============================================================================
// Enums (mirror de backend/src/Compras/Domain/*.cs y SharedKernel/Domain/*.cs)
// ============================================================================

/**
 * 10 estados del agregado <c>Requisicion</c> (doc 01 §5.2): 8 base + 2
 * terminales de cierre manual (ADR-0043). Los 2 terminales se agregan
 * aditivos en el PR #1 (ningún flujo los alcanza aún; el cierre manual
 * que los produce llega en el PR #2).
 */
export const EstadoRequisicion = {
  Borrador: 0,
  EnAutorizacion: 1,
  Autorizada: 2,
  EnSurtido: 3,
  Cerrada: 4,
  Cancelada: 5,
  Rechazada: 6,
  Eliminada: 7,
  CerradaSinSurtir: 8,
  CerradaSurtidaParcial: 9,
} as const satisfies Record<string, number>;
export type EstadoRequisicion =
  (typeof EstadoRequisicion)[keyof typeof EstadoRequisicion];

/**
 * Situación calculada de la RQ dentro de <c>EnSurtido</c> (ADR-0043, el
 * "badge calculado"). NO es un estado de la máquina: se deriva server-side
 * y solo viene poblada cuando <c>estado === EnSurtido</c> (null en otros
 * estados). Espejo del enum backend <c>SituacionSurtido</c>. PR1 solo
 * declara el enum; el consumo en UI llega en PR2.
 */
export const SituacionSurtido = {
  EsperandoCompra: 0,
  ListoParaSurtir: 1,
  SurtidoParcial: 2,
} as const satisfies Record<string, number>;
export type SituacionSurtido =
  (typeof SituacionSurtido)[keyof typeof SituacionSurtido];

/** 4 clasificaciones funcionales de la RQ (doc 01 §10.2.1). */
export const Clasificacion = {
  Servicio: 0,
  OrdenCompra: 1,
  MateriaPrima: 2,
  Pinturas: 3,
} as const satisfies Record<string, number>;
export type Clasificacion = (typeof Clasificacion)[keyof typeof Clasificacion];

/** 3 prioridades (doc 01 §10.2.1). */
export const Prioridad = {
  Baja: 0,
  Normal: 1,
  Alta: 2,
} as const satisfies Record<string, number>;
export type Prioridad = (typeof Prioridad)[keyof typeof Prioridad];

/**
 * Origen de la RQ: <c>Manual</c> (capturada por un usuario) o <c>Sistema</c>
 * (creada por el motor de reabasto, ADR-0047 PR5). Mirror de
 * <c>Compras.Domain.OrigenRequisicion</c>. El FE lo usa para el badge
 * "Sistema" en la bandeja y para el aviso al eliminar.
 */
export const OrigenRequisicion = {
  Manual: 0,
  Sistema: 1,
} as const satisfies Record<string, number>;
export type OrigenRequisicion =
  (typeof OrigenRequisicion)[keyof typeof OrigenRequisicion];

/** Niveles de autorización 1 y 2 (no 0; alineado con el DDL). */
export const NivelAutorizacion = {
  Nivel1: 1,
  Nivel2: 2,
} as const satisfies Record<string, number>;
export type NivelAutorizacion =
  (typeof NivelAutorizacion)[keyof typeof NivelAutorizacion];

/**
 * Bitmask de aplicabilidad de un motivo (doc 01 §3.bis.3). Se compone
 * con OR: <c>Rechazo | Cancelacion = 5</c>.
 */
export const MotivoRechazoAplicaA = {
  Ninguno: 0,
  Rechazo: 1,
  Eliminacion: 2,
  Cancelacion: 4,
  /** F3-PR2: motivos aplicables al flujo de Rechazo de OC. */
  OrdenCompra: 8,
  /** ADR-0043: motivos aplicables al cierre manual de RQ (jefe de almacén). */
  CierreManual: 16,
  Todos: 31, // Rechazo | Eliminacion | Cancelacion | OrdenCompra | CierreManual
} as const satisfies Record<string, number>;
export type MotivoRechazoAplicaA =
  (typeof MotivoRechazoAplicaA)[keyof typeof MotivoRechazoAplicaA] | number;

/**
 * 4 naturalezas del artículo (vive en SharedKernel — no es Compras-
 * específico, pero la matriz A1 §3.bis las consume).
 */
export const Naturaleza = {
  Estandar: 0,
  Servicio: 1,
  Critico: 2,
  Riesgo: 3,
} as const satisfies Record<string, number>;
export type Naturaleza = (typeof Naturaleza)[keyof typeof Naturaleza];

/** Estatus de items del catálogo cross-empresa (artículos, proveedores). */
export const EstatusCatalogo = {
  Activo: 0,
  Inactivo: 1,
  EnRevision: 2,
} as const satisfies Record<string, number>;
export type EstatusCatalogo =
  (typeof EstatusCatalogo)[keyof typeof EstatusCatalogo];

// ============================================================================
// Helpers — número ↔ string legible
// ============================================================================

const ESTADO_LABELS: Record<EstadoRequisicion, string> = {
  [EstadoRequisicion.Borrador]: 'Borrador',
  [EstadoRequisicion.EnAutorizacion]: 'En autorización',
  [EstadoRequisicion.Autorizada]: 'Autorizada',
  [EstadoRequisicion.EnSurtido]: 'En surtido',
  [EstadoRequisicion.Cerrada]: 'Cerrada',
  [EstadoRequisicion.Cancelada]: 'Cancelada',
  [EstadoRequisicion.Rechazada]: 'Rechazada',
  [EstadoRequisicion.Eliminada]: 'Eliminada',
  [EstadoRequisicion.CerradaSinSurtir]: 'Cerrada sin surtir',
  [EstadoRequisicion.CerradaSurtidaParcial]: 'Cerrada surtida parcialmente',
};
export const estadoToString = (e: EstadoRequisicion): string =>
  ESTADO_LABELS[e];

/**
 * Devuelve la clave del enum (no el label humano). Útil para alimentar
 * a <c>obtenerDefinicion</c> del glosario, que indexa por
 * <c>'EnSurtido'</c>, <c>'Borrador'</c>, etc.
 */
const ESTADO_KEYS: Record<EstadoRequisicion, string> = {
  [EstadoRequisicion.Borrador]: 'Borrador',
  [EstadoRequisicion.EnAutorizacion]: 'EnAutorizacion',
  [EstadoRequisicion.Autorizada]: 'Autorizada',
  [EstadoRequisicion.EnSurtido]: 'EnSurtido',
  [EstadoRequisicion.Cerrada]: 'Cerrada',
  [EstadoRequisicion.Cancelada]: 'Cancelada',
  [EstadoRequisicion.Rechazada]: 'Rechazada',
  [EstadoRequisicion.Eliminada]: 'Eliminada',
  [EstadoRequisicion.CerradaSinSurtir]: 'CerradaSinSurtir',
  [EstadoRequisicion.CerradaSurtidaParcial]: 'CerradaSurtidaParcial',
};
export const estadoToKey = (e: EstadoRequisicion): string => ESTADO_KEYS[e];

/**
 * Labels humanos de la situación calculada dentro de <c>EnSurtido</c>
 * (ADR-0043). Espejo de <see cref="SituacionSurtido"/>. Solo se muestran
 * cuando <c>estado === EnSurtido</c> y el backend pobló la situación.
 */
const SITUACION_LABELS: Record<SituacionSurtido, string> = {
  [SituacionSurtido.EsperandoCompra]: 'Esperando compra',
  [SituacionSurtido.ListoParaSurtir]: 'Listo para surtir',
  [SituacionSurtido.SurtidoParcial]: 'Surtido parcial',
};
export const situacionToString = (s: SituacionSurtido): string =>
  SITUACION_LABELS[s];

/**
 * Clave del enum de situación (no el label). Alimenta a
 * <c>obtenerDefinicion</c> del glosario, que indexa por
 * <c>'EsperandoCompra'</c>, etc.
 */
const SITUACION_KEYS: Record<SituacionSurtido, string> = {
  [SituacionSurtido.EsperandoCompra]: 'EsperandoCompra',
  [SituacionSurtido.ListoParaSurtir]: 'ListoParaSurtir',
  [SituacionSurtido.SurtidoParcial]: 'SurtidoParcial',
};
export const situacionToKey = (s: SituacionSurtido): string =>
  SITUACION_KEYS[s];

const NATURALEZA_LABELS: Record<Naturaleza, string> = {
  [Naturaleza.Estandar]: 'Estándar',
  [Naturaleza.Servicio]: 'Servicio',
  [Naturaleza.Critico]: 'Crítico',
  [Naturaleza.Riesgo]: 'Riesgo',
};
export const naturalezaToString = (n: Naturaleza): string =>
  NATURALEZA_LABELS[n];

const NATURALEZA_KEYS: Record<Naturaleza, string> = {
  [Naturaleza.Estandar]: 'Estandar',
  [Naturaleza.Servicio]: 'Servicio',
  [Naturaleza.Critico]: 'Critico',
  [Naturaleza.Riesgo]: 'Riesgo',
};
export const naturalezaToKey = (n: Naturaleza): string => NATURALEZA_KEYS[n];

const CLASIFICACION_LABELS: Record<Clasificacion, string> = {
  [Clasificacion.Servicio]: 'Servicio',
  [Clasificacion.OrdenCompra]: 'Orden de compra',
  [Clasificacion.MateriaPrima]: 'Materia prima',
  [Clasificacion.Pinturas]: 'Pinturas',
};
export const clasificacionToString = (c: Clasificacion): string =>
  CLASIFICACION_LABELS[c];

const PRIORIDAD_LABELS: Record<Prioridad, string> = {
  [Prioridad.Baja]: 'Baja',
  [Prioridad.Normal]: 'Normal',
  [Prioridad.Alta]: 'Alta',
};
export const prioridadToString = (p: Prioridad): string => PRIORIDAD_LABELS[p];

/**
 * Label del nivel de autorización pendiente para la bandeja de pendientes
 * (PR-A). "Falta N1" / "Falta N2" — conciso para una columna/badge.
 */
const NIVEL_PENDIENTE_LABELS: Record<NivelAutorizacion, string> = {
  [NivelAutorizacion.Nivel1]: 'Falta N1',
  [NivelAutorizacion.Nivel2]: 'Falta N2',
};
export const nivelPendienteToString = (n: NivelAutorizacion): string =>
  NIVEL_PENDIENTE_LABELS[n];

// ============================================================================
// DTOs (mirror de los Response records del backend)
// ============================================================================

/**
 * Genérica de paginación offset-based usada por las bandejas (mirror
 * de <c>PagedResponse&lt;T&gt;</c>).
 */
export interface PagedResponse<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

/**
 * Item de bandeja (lista). Mirror de <c>RequisicionListItemResponse</c>.
 * Solo cabecera; el detalle completo se obtiene vía
 * <c>GET /requisiciones/{id}</c>.
 */
export interface RequisicionListItemResponse {
  id: string;
  folio: string;
  folioAnio: number;
  estado: EstadoRequisicion;
  clasificacion: Clasificacion;
  prioridad: Prioridad;
  sucursalId: string;
  departamentoId: string;
  requisitanteId: string;
  descripcion: string | null;
  /** ISO 8601 UTC. */
  fechaSolicitud: string;
  /** Fecha sin hora (formato <c>YYYY-MM-DD</c>). */
  fechaEntregaDeseada: string | null;
  /**
   * Nombres resueltos en backend (ADR-0042) para no depender de leer los
   * catálogos completos en el cliente. <c>null</c> si no se resolvió → el
   * FE cae al id.
   */
  requisitanteNombre: string | null;
  departamentoNombre: string | null;
  departamentoClave: string | null;
  /**
   * Situación calculada de la RQ dentro de <c>EnSurtido</c> (ADR-0043).
   * <c>null</c> fuera de <c>EnSurtido</c>. PR1 solo expone el campo; el
   * badge en UI llega en PR2.
   */
  situacionSurtido: SituacionSurtido | null;
  /**
   * Nivel de autorización pendiente (N1/N2) dentro de <c>EnAutorizacion</c>
   * (PR-A). <c>null</c> fuera de ese estado. Lo deriva el backend con
   * <c>NivelPendienteDerivacion</c>; lo pueblan solo las bandejas que lo
   * muestran (la de pendientes).
   */
  nivelPendiente: NivelAutorizacion | null;
  /**
   * Origen de la RQ (ADR-0047 PR5). <c>Sistema</c> = creada por el motor de
   * reabasto; <c>Manual</c> = capturada por un usuario. La bandeja muestra el
   * badge "Sistema" solo para las automáticas.
   */
  origen: OrigenRequisicion;
}

/**
 * Detalle completo de una RQ (mirror de <c>RequisicionResponse</c>).
 * <c>Lineas</c> y <c>Autorizaciones</c> siempre poblados (al menos
 * lista vacía).
 *
 * <para>El campo <c>version</c> se expone también en el header
 * <c>ETag</c>; el cliente HTTP lo captura y lo guarda en
 * <c>query.meta.etag</c> para mandarlo como <c>If-Match</c> en
 * mutaciones (ver <c>useRequisicion</c>).</para>
 */
export interface RequisicionResponse {
  id: string;
  empresaId: string;
  folio: string;
  folioAnio: number;
  clasificacion: Clasificacion;
  sucursalId: string;
  departamentoId: string;
  /** PR3: nullable. RQ manual = null; solo el reorden (RQ Sistema) lo pobla. */
  almacenDestinoId: string | null;
  requisitanteId: string;
  creadorId: string;
  /**
   * Nombres resueltos en backend (ADR-0042); <c>null</c> si no se resolvió.
   * El <c>creadorId</c> no se resuelve por ahora (fuera de alcance).
   */
  requisitanteNombre: string | null;
  departamentoNombre: string | null;
  departamentoClave: string | null;
  descripcion: string | null;
  prioridad: Prioridad;
  /** ISO 8601 UTC. */
  fechaSolicitud: string;
  /** Fecha sin hora (formato <c>YYYY-MM-DD</c>). */
  fechaEntregaDeseada: string | null;
  proveedorSugeridoId: string | null;
  // Etiqueta del proveedor sugerido resuelta en backend (ADR-0042 addendum).
  proveedorSugeridoRazonSocial: string | null;
  proveedorSugeridoClave: string | null;
  estado: EstadoRequisicion;
  motivoTerminacionId: string | null;
  motivoTerminacionTexto: string | null;
  actorTerminacionId: string | null;
  /** ISO 8601 UTC. */
  fechaTerminacion: string | null;
  /**
   * <c>null</c> mientras la RQ no se haya convertido a OC. Cuando el
   * comprador la convierte (o el handler auto-genera bajo el setting
   * <c>autoGenerarOcAlAutorizar=true</c>), apunta al id de la OC activa.
   * El botón "Convertir a OC" del detalle se oculta cuando esto es no-null.
   */
  comprometidaEnOcId: string | null;
  /**
   * Concurrency token. Se duplica en el header <c>ETag</c> de la
   * response.
   */
  version: number;
  /** ISO 8601 UTC. */
  createdAt: string;
  /** ISO 8601 UTC. */
  updatedAt: string;
  lineas: LineaResponse[];
  autorizaciones: AutorizacionResponse[];
  /**
   * Situación calculada de la RQ dentro de <c>EnSurtido</c> (ADR-0043).
   * <c>null</c> fuera de <c>EnSurtido</c>. PR1 solo expone el campo; el
   * badge en UI llega en PR2.
   */
  situacionSurtido: SituacionSurtido | null;
  /**
   * Origen de la RQ (ADR-0047 PR5). <c>Sistema</c> = creada por el motor de
   * reabasto; <c>Manual</c> = capturada por un usuario. El detalle lo usa para
   * avisar, al eliminar una RQ de sistema, que el motor podría re-proponerla.
   */
  origen: OrigenRequisicion;
}

/**
 * Línea de RQ (mirror de <c>LineaResponse</c>). Incluye el cubrimiento
 * decompuesto para que <c>&lt;CubrimientoBar/&gt;</c> (UF5-PR1) pinte
 * sin lógica duplicada en el frontend.
 *
 * <para><b>Nota</b>: <c>articuloId</c> NO trae embebida la naturaleza
 * del artículo. Para mostrar <c>&lt;NaturalezaBadge/&gt;</c> en una
 * línea hay que joinar contra el catálogo de artículos
 * (<c>useArticulo(articuloId)</c>, llega en UF2-PR1). El backend
 * podría exponerla en <c>LineaResponse</c> en el futuro para evitar
 * el round-trip extra; flageado como hallazgo.</para>
 */
export interface LineaResponse {
  id: string;
  posicion: number;
  articuloId: string;
  // Etiqueta del artículo resuelta en backend (ADR-0042 addendum); null → cae al id.
  articuloClave: string | null;
  articuloNombre: string | null;
  cantidad: number;
  unidadMedida: string;
  precioEstimadoMonto: number;
  precioEstimadoMoneda: string;
  cuentaContableId: string | null;
  centroCostoId: string | null;
  // Etiqueta del CC-Máquina (Dim3) resuelta en backend por IDim3ReadPort (ADR-0042,
  // sin filtro, incluye inactivas); null → el FE cae a "No catalogado" (Fase E PR2).
  centroCostoClave: string | null;
  centroCostoNombre: string | null;
  proyecto: string | null;
  /** Fecha sin hora. */
  fechaRequerida: string | null;
  notas: string | null;
  cantDeAlmacen: number;
  cantDeCompra: number;
  cantRecibida: number;
  cantPendiente: number;
  reservaId: string | null;
  /**
   * Cantidad total ya **entregada** del almacén al solicitante contra
   * esta línea — el acumulador `CantidadEntregada` del dominio (canal de
   * entrega Almacén→Compras, ADR-0043). Derivado del dominio y mapeado
   * por Mapster; ya NO se agrupa por artículo vía `IAlmacenEntregasReadPort`
   * (huérfano desde #3). Opcional sólo por resiliencia ante respuestas
   * viejas en cache → el FE cae a la fórmula con `cantDeAlmacen +
   * cantRecibida` como pendiente.
   *
   * Nota de terminología: "entregar" = salida del almacén al
   * solicitante. NO confundir con "surtir" (recibir del proveedor).
   */
  cantEntregadoDeAlmacen?: number;
  /**
   * Cantidad pendiente de **entregar** al solicitante, derivada del
   * dominio (ADR-0043 #3): `(cantDeAlmacen + cantRecibida) −
   * cantEntregadoDeAlmacen`, clamped a ≥ 0. Incluye el material recibido
   * por compra (vía OC), no sólo el reservado de almacén. La UI del sheet
   * "Nueva salida con RQ" la usa como default de cantidad a entregar.
   */
  cantPendienteEntregar?: number;
}

/** Mirror de <c>AutorizacionResponse</c>. */
export interface AutorizacionResponse {
  id: string;
  nivel: NivelAutorizacion;
  usuarioId: string;
  /** ISO 8601 UTC. */
  fechaHora: string;
  notas: string | null;
}

/** Mirror de <c>MotivoRechazoResponse</c>. */
export interface MotivoRechazoResponse {
  id: string;
  clave: string;
  descripcion: string;
  permiteTextoLibre: boolean;
  /** Bitmask: combinación de <c>MotivoRechazoAplicaA</c>. */
  aplicaA: MotivoRechazoAplicaA;
}

/**
 * Helper para chequear bitmask: <c>aplicaABitmaskIncluye(motivo.aplicaA, MotivoRechazoAplicaA.Rechazo)</c>.
 */
export function aplicaABitmaskIncluye(
  aplicaA: MotivoRechazoAplicaA,
  flag: MotivoRechazoAplicaA,
): boolean {
  return (aplicaA & flag) === flag && flag !== 0;
}

// ============================================================================
// Aprobadores (P9 — UF6-PR1)
// ============================================================================

/**
 * Mirror del enum <c>RolAprobador</c> del backend (Compras.Domain).
 * Doc 05 §11.4 (admin de aprobadores).
 */
export const RolAprobador = {
  /** Autoriza Nivel 1. */
  JefeDpto: 0,
  /** Visa stock-aware (transversal). */
  JefeAlmacen: 1,
  /** Autoriza Nivel 2 (montos altos / críticos). */
  AutorizadorN2: 2,
} as const satisfies Record<string, number>;

export type RolAprobador = (typeof RolAprobador)[keyof typeof RolAprobador];

/**
 * Etiquetas humanas para mostrar en UI. El backend usa los enum values
 * numéricos; el frontend resuelve a string al renderear.
 */
export const ROL_APROBADOR_LABEL: Record<RolAprobador, string> = {
  [RolAprobador.JefeDpto]: 'Jefe de departamento',
  [RolAprobador.JefeAlmacen]: 'Jefe de almacén',
  [RolAprobador.AutorizadorN2]: 'Autorizador Nivel 2',
};

/** Mirror de <c>AprobadorVigenteResponse</c>. */
export interface AprobadorVigenteResponse {
  id: string;
  departamentoId: string;
  rol: RolAprobador;
  usuarioId: string;
  /** ISO 8601 UTC. */
  vigenteDesde: string;
  designadoPor: string;
  motivo: string | null;
}

/** Mirror de <c>AprobadorHistoricoResponse</c>. */
export interface AprobadorHistoricoResponse {
  id: string;
  departamentoId: string;
  rol: RolAprobador;
  usuarioId: string;
  vigenteDesde: string;
  /** ISO 8601 UTC; <c>null</c> = todavía vigente. */
  vigenteHasta: string | null;
  designadoPor: string;
  motivo: string | null;
}

/** Mirror de <c>DesignarAprobadorResponse</c>. */
export interface DesignarAprobadorResponse {
  id: string;
  vigenteDesde: string;
}

// ============================================================================
// Histórico (P3 timeline — UF7-PR4, doc 05 §14.1)
// ============================================================================

/**
 * Mirror del enum <c>HistoricoTipo</c> del backend
 * (<c>Compras.Application.Historico</c>). Define los tipos de
 * transición que el agregado <c>Requisicion</c> registra en
 * <c>core.audit_log</c>.
 *
 * <para>El backend serializa estos enums como número (igual que
 * <see cref="EstadoRequisicion"/> y compañía); declarar como
 * <c>as const</c> mantiene tipos estrictos sin parser intermedio.</para>
 */
export const HistoricoTipo = {
  /**
   * Fallback genérico del mapper backend (<c>HistoricoTipoMapper</c>):
   * un update de <c>Requisicion</c> que NO es transición de estado
   * (editar cabecera, notas, etc.). <b>Sentinel = 0</b>: debe ir primero
   * para que TODOS los valores siguientes coincidan con el enum backend
   * (<c>Compras.Application.Historico.HistoricoTipo</c>). Omitirlo corre
   * el mirror +1 y desincroniza las etiquetas. El guard
   * <c>enum-contract.test.ts</c> protege este alineamiento.
   */
  Cambio: 0,
  Creada: 1,
  LineaAgregada: 2,
  LineaActualizada: 3,
  LineaEliminada: 4,
  Transmitida: 5,
  AutorizadaN1: 6,
  AutorizadaN2: 7,
  Rechazada: 8,
  Eliminada: 9,
  Cancelada: 10,
  CubrimientoRegistrado: 11,
  RecepcionRegistrada: 12,
  SaldoNoSurtido: 13,
  Cerrada: 14,
} as const satisfies Record<string, number>;
export type HistoricoTipo = (typeof HistoricoTipo)[keyof typeof HistoricoTipo];

/**
 * Etiqueta humana por tipo de transición (UI-only). El backend
 * devuelve solo el número; el frontend resuelve a string al
 * renderizar.
 */
export const HISTORICO_TIPO_LABEL: Record<HistoricoTipo, string> = {
  [HistoricoTipo.Cambio]: 'Modificada',
  [HistoricoTipo.Creada]: 'Creada',
  [HistoricoTipo.LineaAgregada]: 'Línea agregada',
  [HistoricoTipo.LineaActualizada]: 'Línea actualizada',
  [HistoricoTipo.LineaEliminada]: 'Línea eliminada',
  [HistoricoTipo.Transmitida]: 'Transmitida a autorización',
  [HistoricoTipo.AutorizadaN1]: 'Autorizada Nivel 1',
  [HistoricoTipo.AutorizadaN2]: 'Autorizada Nivel 2',
  [HistoricoTipo.Rechazada]: 'Rechazada',
  [HistoricoTipo.Eliminada]: 'Eliminada',
  [HistoricoTipo.Cancelada]: 'Cancelada',
  [HistoricoTipo.CubrimientoRegistrado]: 'Cubrimiento registrado',
  [HistoricoTipo.RecepcionRegistrada]: 'Recepción registrada',
  [HistoricoTipo.SaldoNoSurtido]: 'Saldo no surtido',
  [HistoricoTipo.Cerrada]: 'Cerrada',
};

/**
 * Mirror de <c>HistoricoEntryResponse</c> (backend
 * <c>Compras.Application.Historico</c>). Una entrada por transición
 * del agregado, ordenadas por timestamp ascendente desde el endpoint.
 *
 * <para><c>cambios</c> viene como string JSON crudo (diff completo);
 * el caller lo parsea solo si va a expandir el detalle (accordion).
 * Mantenerlo como string evita tipar el universo de payloads
 * posibles.</para>
 */
export interface HistoricoEntryResponse {
  tipo: HistoricoTipo;
  /** "crear" | "actualizar" | "borrar". */
  operacion: string;
  /** "Requisicion" | "LineaRequisicion" | "Autorizacion" | etc. */
  entidad: string;
  /** ID de la entidad afectada (línea, autorización, etc.). Puede
   * ser <c>null</c> en eventos a nivel del agregado. */
  entidadId: string | null;
  /** Usuario que disparó la transición. <c>null</c> en eventos
   * automáticos del sistema. */
  actorId: string | null;
  /** Nombre del actor resuelto en backend (ADR-0042). <c>null</c> si la
   * entrada no tiene actor (sistema → mostrar "Sistema") o si no se resolvió
   * (service principal / usuario borrado → caer al <c>actorId</c>). */
  actorNombre: string | null;
  /** ISO 8601 UTC. */
  timestamp: string;
  /** JSON serializado con el diff. El caller lo parsea on-demand. */
  cambios: string;
  /** Correlation id del comando original. Útil para trazar. */
  correlationId: string;
}

/**
 * Preview read-only del cubrimiento estimado de una RQ (PR-C). Mirror de
 * <c>PreviewCubrimientoResponse</c> del backend. <c>aplica</c> es <c>false</c>
 * (con <c>lineas</c> vacía) fuera de <c>EnAutorizacion</c>. Las cantidades son
 * una <b>estimación</b> al instante de la consulta — no una reserva: el stock
 * es móvil hasta autorizar.
 */
export interface PreviewCubrimientoResponse {
  requisicionId: string;
  aplica: boolean;
  lineas: PreviewCubrimientoLinea[];
}

/** Estimación por línea: cuánto se cubriría de almacén vs. iría a compra. */
export interface PreviewCubrimientoLinea {
  lineaId: string;
  articuloId: string;
  // Etiqueta del artículo resuelta en backend (ADR-0042 addendum); null → el FE
  // cae al id. Igual que las líneas del detalle de RQ.
  articuloClave: string | null;
  articuloNombre: string | null;
  cantidad: number;
  estimadoDeAlmacen: number;
  estimadoDeCompra: number;
  disponible: number;
}
