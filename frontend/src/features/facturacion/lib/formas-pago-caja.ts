import type { FormaPagoItem } from '@/modules/catalogos/api/types';

export function formasPagoCaja(catalogo: readonly FormaPagoItem[]): FormaPagoItem[] {
  return catalogo.filter((forma) => forma.activa);
}

export function formasPagoVigentes(
  claves: readonly string[],
  disponibles: readonly FormaPagoItem[],
): boolean {
  return (
    claves.length > 0 &&
    claves.every((clave) => disponibles.some((forma) => forma.activa && forma.claveSat === clave))
  );
}
