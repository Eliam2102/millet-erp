/** La diferencia positiva conserva saldo del cliente; solo el faltante usa tolerancia. */
/** M-326: mismo tope que `PropuestaAplicacionPago.ToleranciaRedondeo` del backend. */
export const TOLERANCIA_REDONDEO = 0.99;

export function diferenciaDeposito(monto: number, aplicado: number, tolerancia: number | null) {
  const diferencia = Math.round((monto - aplicado) * 100) / 100;
  // M-326: un faltante de hasta $0.99 (en la moneda del cobro) es redondeo: va a la cuenta REDONDEO sin motivo (lo autoriza el Gerente de Área) y no
  // depende de la tolerancia no fiscal. Supuesto a validar con Contabilidad.
  const esRedondeo = diferencia < 0 && -diferencia <= TOLERANCIA_REDONDEO;
  // M-326 parte 2: un excedente de hasta $0.99 (el cliente depositó centavos de más) también es redondeo: va a la cuenta REDONDEO,
  // no a saldo a favor, y lo autoriza el Gerente de Área.
  const esExcedenteRedondeo = diferencia > 0 && diferencia <= TOLERANCIA_REDONDEO;
  return {
    diferencia,
    saldoAFavor: esExcedenteRedondeo ? 0 : Math.max(0, diferencia),
    esRedondeo,
    esExcedenteRedondeo,
    faltaTolerancia: !esRedondeo && diferencia < 0 && tolerancia === null,
    esAjusteNoFiscal: !esRedondeo && diferencia < 0 && tolerancia !== null && -diferencia < tolerancia,
    excedeTolerancia: !esRedondeo && diferencia < 0 && tolerancia !== null && -diferencia >= tolerancia,
  };
}
