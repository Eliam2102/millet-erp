import { humanizarEstado } from '@/components/erp/trazabilidad/humanizar-estado';

/**
 * Traducción de los textos técnicos del historial de la OC a español de
 * negocio. El backend arma <c>resumen</c> como "Creó {Entidad} {etiqueta}"
 * (+ " — Campo: antes → después" en actualizaciones); aquí se convierte
 * a "Creó la firma de autorización" sin id ni nombres de clase.
 */

/** Entidades conocidas de la OC, con artículo para que la frase suene natural. */
const ETIQUETAS_ENTIDAD: Readonly<Record<string, string>> = {
  OrdenCompra: 'la orden de compra',
  AutorizacionOC: 'la firma de autorización',
  LineaOrdenCompra: 'una línea de la orden',
  AdjuntoOC: 'un adjunto de la orden',
  OrdenCompraPdf: 'el PDF de la orden',
};

const ACCION_POR_OPERACION: Readonly<Record<string, string>> = {
  crear: 'Se creó',
  actualizar: 'Se modificó',
  borrar: 'Se eliminó',
  eliminar: 'Se eliminó',
};

/** Cabecera del resumen: verbo + entidad + etiqueta opcional. */
const CABECERA_RESUMEN = /^(Creó|Modificó|Eliminó)\s+(\S+)(?:\s+(.*))?$/;

/** Id/hash crudo (8 hex, o GUID completo) que el backend agrega como etiqueta. */
const ID_CRUDO = /^[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){0,3}(?:-[0-9a-fA-F]{12})?$/;

/** Palabras compuestas en PascalCase (EnAutorizacionJefeCompras, OrdenCompra). */
const PALABRA_COMPUESTA = /\b[A-Z][a-z]+(?:[A-Z][a-z]+)+\b/g;

/** Nombre de entidad sin mapa: separa PascalCase en palabras en minúsculas. */
function nombreLegible(entidad: string): string {
  return entidad
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .toLowerCase();
}

/** Frase de la entidad (con artículo cuando se conoce): "la firma de autorización". */
export function etiquetaEntidadHistorial(entidad: string): string {
  return ETIQUETAS_ENTIDAD[entidad] ?? nombreLegible(entidad);
}

/** Encabezado de un evento: "Se creó la orden de compra". */
export function accionHistorial(operacion: string, entidad: string): string {
  const accion = ACCION_POR_OPERACION[operacion] ?? operacion;
  return `${accion} ${etiquetaEntidadHistorial(entidad)}`;
}

/** Estados y otros identificadores PascalCase del texto a palabras legibles. */
function humanizarPalabras(texto: string): string {
  return texto.replace(PALABRA_COMPUESTA, (palabra) => humanizarEstado(palabra));
}

/**
 * Quita del texto de la etiqueta el nombre de la entidad repetido y el id
 * crudo. Si no queda nada útil (caso OC: "AutorizacionOC 01a12f96") devuelve "".
 */
function etiquetaUtil(entidad: string, etiqueta: string): string {
  let resto = etiqueta.trim();
  while (resto === entidad || resto.startsWith(`${entidad} `)) {
    resto = resto.slice(entidad.length).trim();
  }
  return ID_CRUDO.test(resto) ? '' : resto;
}

/**
 * Convierte el <c>resumen</c> del backend a texto legible. Devuelve
 * <c>null</c> si no hay texto. Si el formato no es el esperado, sólo
 * humaniza las palabras PascalCase y conserva el resto tal cual.
 */
export function resumenHistorialLegible(resumen: string | null | undefined): string | null {
  if (!resumen?.trim()) return null;

  const separador = resumen.indexOf(' — ');
  const cabecera = (separador === -1 ? resumen : resumen.slice(0, separador)).trim();
  const detalle = separador === -1 ? null : resumen.slice(separador + 3);

  const coincidencia = CABECERA_RESUMEN.exec(cabecera);
  if (!coincidencia) return humanizarPalabras(resumen);

  const [, verbo, entidad, etiqueta = ''] = coincidencia;
  const util = etiquetaUtil(entidad, etiqueta);
  const frase = `${verbo} ${etiquetaEntidadHistorial(entidad)}${util ? ` ${util}` : ''}`;

  return detalle === null ? frase : `${frase} — ${humanizarPalabras(detalle)}`;
}
