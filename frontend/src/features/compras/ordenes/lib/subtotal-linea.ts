import { DescuentoTipo } from '../api/types';

/** Redondeo a centavos al par, igual que el dominio de OC. */
function centavosAlPar(valor: number): number {
  const escalado = valor * 100;
  const entero = Math.floor(escalado);
  const mitad = Math.abs(escalado - entero - 0.5) <= Number.EPSILON * Math.max(1, Math.abs(escalado)) * 2;
  return (mitad ? entero + (entero % 2) : Math.round(escalado)) / 100;
}

/** Vista previa; el subtotal persistido sigue siendo autoridad del backend. */
export function subtotalLinea(
  cantidad: number,
  precio: number,
  tipo?: DescuentoTipo | null,
  valor?: number | null,
): number {
  const bruto = centavosAlPar((Number(cantidad) || 0) * (Number(precio) || 0));
  const descuento = Number(valor) || 0;
  if (bruto <= 0) return bruto;
  if (tipo === DescuentoTipo.Porcentaje) return bruto - centavosAlPar(bruto * descuento / 100);
  if (tipo === DescuentoTipo.Monto) return bruto - Math.min(descuento, bruto);
  return bruto;
}

export function etiquetaDescuento(tipo?: DescuentoTipo, valor?: number): string {
  if (valor == null) return 'Descuento no informado';
  if (!valor) return 'Sin descuento';
  return tipo === DescuentoTipo.Porcentaje
    ? `Descuento: ${valor.toFixed(2)} %`
    : `Descuento: $${valor.toFixed(2)}`;
}
