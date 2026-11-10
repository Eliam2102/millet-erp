/**
 * M19: el proveedor de timbrado (FiscalAPI) rechazó por saldo insuficiente. No es un error de los
 * datos fiscales: lo recarga Sistemas y después se reintenta. El backend manda este código con el
 * mensaje ya en español.
 */
export const CODIGO_PAC_SIN_SALDO = 'PAC_SIN_SALDO';

export const TEXTO_PAC_SIN_SALDO =
  'El proveedor de timbrado no tiene saldo para timbrar. No es un error de los datos fiscales: avisa a Sistemas para que recargue los timbres y después reintenta.';

export function esPacSinSaldo(codigo: string | null | undefined): boolean {
  return codigo?.trim().toUpperCase() === CODIGO_PAC_SIN_SALDO;
}
