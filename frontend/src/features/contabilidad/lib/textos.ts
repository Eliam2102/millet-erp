/**
 * Textos para el usuario de la importación: nombres legibles de columnas, de hallazgos y de los datos del
 * perfilado. Los códigos internos (`CONTAB_*`, nombres de columna canónicos) no se muestran en pantalla;
 * quedan en el `title` (para soporte) y en el reporte descargable.
 */

import type { CuentaControl, OrigenMovimiento, TipoCuenta, ValidacionMovimiento } from '../api/types';

const COLUMNAS: Record<string, string> = {
  codigo: 'Código',
  nombre: 'Nombre',
  codigo_padre: 'Cuenta padre',
  naturaleza: 'Naturaleza',
  tipo_cuenta: 'Tipo (acumula o afectable)',
  cuenta_control: 'Cuenta colectiva',
  codigo_agrupador: 'Código agrupador',
  grupo_reporte: 'Reporte',
  codigo_origen: 'Código de origen',
  fuente: 'Fuente',
};

const HALLAZGOS: Record<string, string> = {
  CONTAB_IMPORT_COLUMNA_FALTANTE: 'Falta una columna',
  CONTAB_IMPORT_COLUMNA_IGNORADA: 'Columna no reconocida',
  CONTAB_IMPORT_COLUMNA_SIN_MAPEO: 'Columna que no se carga',
  CONTAB_IMPORT_CAMPO_PENDIENTE: 'Dato pendiente de validación',
  CONTAB_IMPORT_CODIGO_FORMATO: 'Código con formato distinto',
  CONTAB_IMPORT_CODIGO_RELLENADO: 'Código completado con ceros',
  CONTAB_IMPORT_NATURALEZA_DESCONOCIDA: 'Naturaleza no reconocida',
  CONTAB_IMPORT_TIPO_DESCONOCIDO: 'Tipo no reconocido',
  CONTAB_IMPORT_PADRE_INEXISTENTE: 'Cuenta padre inexistente',
  CONTAB_IMPORT_CODIGO_DUPLICADO_EN_ARCHIVO: 'Código repetido en el archivo',
  CONTAB_IMPORT_CICLO: 'Jerarquía circular',
  CONTAB_IMPORT_CONTROL_CONFLICTO: 'Cuenta colectiva no coincide',
  CONTAB_IMPORT_FILA_TITULO: 'Título de reporte',
  CONTAB_IMPORT_TIPO_DERIVADO: 'Tipo calculado por la jerarquía',
  CONTAB_IMPORT_PADRE_CONVERTIDO: 'La cuenta padre pasará a acumular',
  CONTAB_CUENTA_RUBRO_INVALIDO: 'Rubro de reporte no válido',
  CONTAB_IMPORT_CODIFICACION: 'Caracteres del archivo',
  CONTAB_IMPORT_NIVEL_DISCREPANTE: 'Nivel distinto al del código',
  CONTAB_IMPORT_ORIGEN_CODIGO_DISTINTO: 'Código de origen ya usado',
  CONTAB_CUENTA_NIVEL_EXCEDIDO: 'Demasiados niveles',
  CONTAB_CUENTA_PADRE_NO_ES_TITULO: 'La cuenta padre recibe movimientos',
  CONTAB_CUENTA_PADRE_INVALIDO: 'Cuenta padre inactiva',
  CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO: 'Cuenta con movimientos',
  CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE: 'Colectiva solo en cuentas que reciben movimientos',
  CONTAB_CUENTA_AFECTABLE_CON_HIJAS: 'Cuenta afectable con hijas',
  CONTAB_CUENTA_NATURALEZA_INVALIDA: 'Naturaleza distinta a la del padre',
};

const DATOS: Record<string, string> = {
  filasLeidas: 'Filas leídas',
  vacias: 'Filas vacías',
  invalidas: 'Filas con errores',
  codigosRellenados: 'Códigos completados con ceros',
  huerfanas: 'Cuentas sin padre en el archivo',
  ciclos: 'Jerarquías circulares',
  duplicadosCodigo: 'Códigos repetidos',
  duplicadosOrigen: 'Códigos de origen repetidos',
  titulosSinHijos: 'Títulos sin cuentas hijas',
  afectablesConHijos: 'Afectables con cuentas hijas',
  profundidadMaxima: 'Niveles del archivo',
  nivelMaximo: 'Niveles permitidos',
  sinNaturaleza: 'Sin naturaleza',
  pendientes: 'Total pendientes',
  filasTitulo: 'Títulos de reporte (no se cargan)',
  rubros: 'Rubros de reporte',
  cuentasConRubro: 'Cuentas de nivel 1 con rubro',
  acumulan: 'Acumulan (no reciben movimientos)',
  afectables: 'Afectables (reciben movimientos)',
  padresAConvertir: 'Cuentas que pasarán a acumular',
};

/** Datos técnicos del perfilado que no aportan al usuario (la huella identifica el archivo para el sistema). */
const DATOS_OCULTOS = new Set(['huella']);

/** P19: etiquetas de usuario del tipo calculado. */
export const ETIQUETA_TIPO: Record<TipoCuenta, string> = {
  Titulo: 'Acumula (no recibe movimientos)',
  Afectable: 'Afectable (recibe movimientos)',
};

/** P23: el módulo autorizado es el SUPUESTO por defecto de la configuración (pendiente del TL). */
export const ETIQUETA_COLECTIVA: Record<CuentaControl, string> = {
  Ninguna: 'No es colectiva',
  Clientes: 'Clientes (solo desde cuentas por cobrar)',
  Deudores: 'Deudores (solo desde cuentas por cobrar)',
  Proveedores: 'Proveedores (solo desde cuentas por pagar)',
  Acreedores: 'Acreedores (solo desde cuentas por pagar)',
};

export const ETIQUETA_ORIGEN: Record<OrigenMovimiento, string> = {
  Manual: 'Captura manual',
  AuxiliarCxC: 'Módulo de cuentas por cobrar',
  AuxiliarCxP: 'Módulo de cuentas por pagar',
};

/** Resultado de la prueba de movimiento en lenguaje de usuario. */
export function textoValidacion(r: ValidacionMovimiento, origen: OrigenMovimiento): string {
  if (r.valida) return `La cuenta acepta movimientos desde «${ETIQUETA_ORIGEN[origen]}».`;
  switch (r.motivo) {
    case 'Titulo':
      return 'La cuenta acumula (es de nivel 1 o tiene cuentas debajo). Los movimientos se registran en sus cuentas afectables.';
    case 'Inactiva':
      return 'La cuenta está inactiva.';
    case 'PendienteValidacion':
      return 'La cuenta está pendiente de validación (falta indicar si es deudora o acreedora).';
    case 'NoAfectableManual':
      return 'La cuenta no admite asientos manuales. Utiliza el movimiento del módulo correspondiente.';
    case 'ControlSoloAuxiliar':
      return `Es una cuenta colectiva de ${(r.cuenta?.cuentaControl ?? 'clientes o proveedores').toLowerCase()}; solo se afecta desde su módulo, que lleva el detalle por persona.`;
    case 'Rubro':
      return 'Es un rubro de reporte; agrupa cuentas para el reporte, pero no recibe movimientos.';
    default:
      return 'La cuenta no existe.';
  }
}

export function nombreColumna(c: string | null | undefined): string {
  if (!c) return '';
  if (COLUMNAS[c]) return COLUMNAS[c];
  // Encabezado del archivo sin traducción (p. ej. «nivel_de_cuenta_sat»): legible, sin guiones bajos.
  const texto = c.replace(/_/g, ' ');
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

export function tituloHallazgo(codigo: string): string {
  return HALLAZGOS[codigo] ?? 'Revisión requerida';
}

export function severidadLegible(severidad: string): string {
  return severidad === 'Error' ? 'Error' : 'Aviso';
}

/** Etiqueta del dato o null si no debe mostrarse. Claves sin traducción: se separan las palabras. */
export function etiquetaDato(clave: string): string | null {
  if (DATOS_OCULTOS.has(clave)) return null;
  if (DATOS[clave]) return DATOS[clave];
  const texto = clave.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}
