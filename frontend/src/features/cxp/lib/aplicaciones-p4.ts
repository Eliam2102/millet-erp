export function calcularAplicacion(saldoFactura: number, saldoDocumento: number, monto: number, monedaFactura: string, monedaDocumento: string, reconocido = 0) {
  const descuento = Math.max(0, monto - reconocido);
  const error = monedaFactura !== monedaDocumento ? 'El documento y la factura deben tener la misma moneda.'
    : !Number.isFinite(monto) || monto <= 0 ? 'Captura un importe mayor a cero.'
    : monto > saldoDocumento ? 'El importe excede el saldo disponible del documento.'
    : descuento > saldoFactura ? 'El importe excede el saldo pendiente de la factura.' : null;
  return { saldoAntes: saldoFactura, saldoDespues: saldoFactura - descuento, descuento, error };
}
