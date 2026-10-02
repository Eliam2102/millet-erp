/**
 * Mirror de los DTOs de Contabilidad (F1-CON-01, docs/modulos/contabilidad/03-contrato-api.md).
 * Los enums viajan como texto. `null` en naturaleza/tipo = pendiente de validación (nunca se supone un valor).
 */
export type Naturaleza = 'Deudora' | 'Acreedora';
export type TipoCuenta = 'Titulo' | 'Afectable';
export type CuentaControl = 'Ninguna' | 'Clientes' | 'Proveedores';
export type FiltroEstatus = '' | 'Activo' | 'Inactivo';

export interface OrigenCuenta {
  fuente: string;
  codigoOrigen: string;
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
  codigoAgrupador: string | null;
  grupoReporte: string | null;
  pendienteValidacion: boolean;
  version: number;
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
  codigoAgrupador: string | null;
  grupoReporte: string | null;
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
