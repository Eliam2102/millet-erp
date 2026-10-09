/**
 * Mirror de los DTOs de Contabilidad (F1-CON-01, docs/modulos/contabilidad/03-contrato-api.md).
 * Los enums viajan como texto. `null` en naturaleza/tipo = pendiente de validación (nunca se supone un valor).
 */
export type Naturaleza = 'Deudora' | 'Acreedora';
/** P19: lo calcula el sistema por la jerarquía. `Titulo` = acumula (no recibe movimientos). */
export type TipoCuenta = 'Titulo' | 'Afectable';
/** P23: cuentas colectivas; solo se afectan desde su módulo. */
export type CuentaControl = 'Ninguna' | 'Clientes' | 'Deudores' | 'Proveedores' | 'Acreedores';
/** P24: un rubro es una agrupación de reporte fuera del árbol de niveles. */
export type ClaseCuenta = 'Cuenta' | 'Rubro';
export type OrigenMovimiento = 'Manual' | 'AuxiliarCxC' | 'AuxiliarCxP';
export type MotivoRechazo = 'NoExiste' | 'Titulo' | 'Inactiva' | 'ControlSoloAuxiliar' | 'PendienteValidacion' | 'Rubro' | 'NoAfectableManual';
export type FiltroEstatus = '' | 'Activo' | 'Inactivo';

export interface OrigenCuenta {
  fuente: string;
  codigoOrigen: string;
}

/** Opción 2 del alta manual: código sugerido (editable) para una hija del padre; null + motivo si no se puede inferir. */
export interface SiguienteCodigo {
  codigo: string | null;
  motivo: string | null;
}

export interface Cuenta {
  id: string;
  codigo: string;
  nombre: string;
  padreId: string | null;
  nivel: number;
  naturaleza: Naturaleza | null;
  tipo: TipoCuenta | null;
  estatus: string;
  activa: boolean;
  cuentaControl: CuentaControl;
  noAfectableManual: boolean;
  codigoAgrupador: string | null;
  grupoReporte: string | null;
  pendienteValidacion: boolean;
  version: number;
  clase: ClaseCuenta;
  /** Rubro de reporte de una cuenta de nivel 1 (P24). */
  rubroId: string | null;
  usada?: boolean | null;
  origenes?: OrigenCuenta[] | null;
}

export interface NodoArbol {
  id: string;
  codigo: string;
  nombre: string;
  nivel: number;
  tipo: TipoCuenta | null;
  activa: boolean;
  pendienteValidacion: boolean;
  tieneHijos: boolean;
}

export interface PaginaCuentas {
  items: Cuenta[];
  total: number;
  offset: number;
  limit: number;
}

export interface FiltrosLista {
  estatus?: FiltroEstatus;
  tipo?: TipoCuenta | '';
  q?: string;
  padreId?: string;
  pendientes?: boolean;
  clase?: ClaseCuenta;
  rubroId?: string;
  offset?: number;
  limit?: number;
}

/** Cuerpo de crear/editar (el código solo viaja al crear). */
export interface CuentaBody {
  codigo?: string;
  nombre: string;
  padreId: string | null;
  naturaleza: Naturaleza | null;
  tipo: TipoCuenta | null;
  cuentaControl: CuentaControl;
  noAfectableManual: boolean;
  codigoAgrupador: string | null;
  grupoReporte: string | null;
  rubroId: string | null;
}

/** Respuesta de POST /cuentas/validar-movimiento (mismo puerto que usan los módulos). */
export interface ValidacionMovimiento {
  valida: boolean;
  motivo: MotivoRechazo | null;
  cuenta: { id: string; codigo: string; nombre: string; cuentaControl: CuentaControl } | null;
}

// ─── Importación ─────────────────────────────────────────────────────────────

export interface ErrorFila {
  fila: number;
  columna: string | null;
  codigo: string;
  severidad: string; // 'Error' | 'Advertencia'
  mensaje: string;
  sugerencia: string;
}

export interface FilaResultado {
  fila: number;
  accion: string;
  errores: ErrorFila[];
}

export interface ResumenImportacion {
  leidas: number;
  vacias: number;
  crear: number;
  actualizar: number;
  sinCambios: number;
  rechazadas: number;
  errores: number;
  advertencias: number;
  /** Filas de título de reporte sin código (no se cargan). */
  omitidas: number;
}

export interface VistaPrevia {
  resumen: ResumenImportacion;
  huella: string;
  puedeAplicar: boolean;
  archivo: ErrorFila[];
  filas: FilaResultado[];
}

export interface ErrorAgrupado {
  codigo: string;
  severidad: string;
  conteo: number;
  ejemplos: { fila: number; columna: string | null }[];
}

export interface Perfil {
  resumen: Record<string, unknown>;
  distribuciones: Record<string, unknown>;
  estructura: Record<string, unknown>;
  control: Record<string, unknown>;
  pendientesValidacion: Record<string, number>;
  nivelContable: Record<string, unknown>;
  columnasSinMapeo: { columna: string; filasConValor: number; valoresDistintos: number }[];
  porCodigoError: ErrorAgrupado[];
  queSeReabre: { hallazgo: string; decision: string }[];
}

export interface LoteImportacion {
  id: string;
  fuente: string;
  archivoNombre: string | null;
  huella: string;
  totalFilas: number;
  creadas: number;
  actualizadas: number;
  sinCambios: number;
  aplicadoEn: string;
  aplicadoPor: string | null;
}

export interface ResultadoAplicar {
  solicitudId?: string | null;
  idempotente: boolean;
  lote: LoteImportacion | null;
}

/** Cuerpo de importación: CSV en base64 (el servidor decodifica) o tabla de texto (xlsx leído en el cliente). */
export interface CuerpoImportacion {
  fuente?: string;
  archivoNombre?: string;
  csvBase64?: string;
  columnas?: string[];
  filas?: (string | null)[][];
  huella?: string;
}

export interface CambioCuenta { antes: Cuenta | null; despues: Cuenta }
export interface SolicitudCatalogo {
  id: string;
  operacion: 'Alta' | 'Cambio' | 'Baja' | 'Reactivacion' | 'Importacion';
  estado: 'Pendiente' | 'Autorizada' | 'Rechazada';
  preparadaPorId: string;
  preparadaPor: string;
  preparadaEn: string;
  resueltaPorId: string | null;
  resueltaPor: string | null;
  resueltaEn: string | null;
  motivoRechazo: string | null;
  version: number;
  cambios: CambioCuenta[];
}
