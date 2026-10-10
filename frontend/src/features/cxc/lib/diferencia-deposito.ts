/** La diferencia positiva conserva saldo del cliente; solo el faltante usa tolerancia. */
export function diferenciaDeposito(monto: number, aplicado: number, tolerancia: number | null) {
  const diferencia = Math.round((monto - aplicado) * 100) / 100;
  return {
    diferencia,
    saldoAFavor: Math.max(0, diferencia),
    faltaTolerancia: diferencia < 0 && tolerancia === null,
    esAjusteNoFiscal: diferencia < 0 && tolerancia !== null && -diferencia < tolerancia,
    excedeTolerancia: diferencia < 0 && tolerancia !== null && -diferencia >= tolerancia,
  };
}
