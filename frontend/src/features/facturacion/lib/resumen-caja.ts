import type { CajaSesionDetalleResponse } from '../api/types';

export function resumirSesionCaja(sesion: CajaSesionDetalleResponse) {
  const efectivo =
    sesion.estado === 'EnArqueo'
      ? (sesion.cortes.find((corte) => corte.formaPago === '01')?.montoSistema ?? 0)
      : (sesion.totalesPorForma.find((total) => total.formaPago === '01')?.montoSistema ??
        sesion.fondoApertura);
  const resumen = {
    efectivo,
    fondo: 0,
    cobrosEfectivo: 0,
    retiros: 0,
    depositos: 0,
    ajustes: 0,
    cobradoSesion: 0,
    numeroCobros: 0,
    otrasFormas: sesion.totalesPorForma
      .filter((total) => total.formaPago !== '01')
      .map((total) => ({ formaPago: total.formaPago, monto: total.montoSistema })),
  };
  const cobros = new Set<string>();
  for (const movimiento of sesion.movimientos) {
    const { tipo, importe, formaPago, cobroMostradorId } = movimiento;
    if (tipo === 'CobroCliente') {
      if (cobroMostradorId == null) resumen.numeroCobros += 1;
      else cobros.add(cobroMostradorId);
    }
    if (tipo === 'CobroCliente' || tipo === 'ReversaCobro') {
      resumen.cobradoSesion += importe;
      if (formaPago === '01') resumen.cobrosEfectivo += importe;
    }
    if (formaPago !== '01') continue;
    if (tipo === 'FondoApertura') resumen.fondo += importe;
    if (tipo === 'Retiro') resumen.retiros += importe;
    if (tipo === 'Deposito') resumen.depositos += importe;
    if (tipo === 'AjusteCorreccion') resumen.ajustes += importe;
  }
  resumen.numeroCobros += cobros.size;
  return resumen;
}
