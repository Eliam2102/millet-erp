import { useMemo } from 'react';

/**
 * Convención ÚNICA de display del CC-Máquina en documentos (ADR-0050,
 * CC-G4 cerrado): `clave — nombre` (em dash con espacios, la convención del
 * feature centros-costo), SIN jerarquía — la jerarquía es de los reportes de
 * la Fase F. Centraliza aquí el formato y el fallback "No catalogado" para
 * que RQ/OC/entrada/salida rendericen la máquina idéntico y en un solo lugar.
 *
 * El backend resuelve el nombre por el read-port sin filtro de alcance
 * (`IDim3ReadPort`), incluyendo inactivas (ADR-0049); el FE solo formatea lo
 * que ya viene resuelto. "No catalogado" es para el dato roto irresoluble
 * (un id sin fila), no para una máquina inactiva —esa sí resuelve su nombre.
 */
export interface CcMaquinaLabelInput {
  clave?: string | null;
  nombre?: string | null;
}

/** Formatea el CC-Máquina; función pura (usable fuera de React: PDF, Excel). */
export function formatCcMaquinaLabel(
  item: CcMaquinaLabelInput | null | undefined,
): string {
  if (item?.clave && item?.nombre) return `${item.clave} — ${item.nombre}`;
  if (item?.clave) return item.clave;
  return 'No catalogado';
}

/** Hook memoizado sobre {@link formatCcMaquinaLabel}. */
export function useCcMaquinaLabel(
  item: CcMaquinaLabelInput | null | undefined,
): string {
  return useMemo(() => formatCcMaquinaLabel(item), [item?.clave, item?.nombre]);
}
