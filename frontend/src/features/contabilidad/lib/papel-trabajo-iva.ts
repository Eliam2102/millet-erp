import type { DiferenciaFiscalIva } from '../api/papel-trabajo-iva';

export const ACUSE_MAXIMO_BYTES = 5 * 1024 * 1024;

/** Valida el acuse antes de subirlo: debe traer contenido y no pasar de 5 MB. Devuelve el mensaje de error o null. */
export function errorAcuse(archivo: File | null): string | null {
  if (!archivo) return 'Elige el archivo del acuse.';
  if (archivo.size === 0) return 'El archivo está vacío.';
  if (archivo.size > ACUSE_MAXIMO_BYTES) return 'El acuse no puede pasar de 5 MB.';
  return null;
}

export const pendientes = (diferencias: DiferenciaFiscalIva[]) => diferencias.filter((d) => d.estado === 'Pendiente');
