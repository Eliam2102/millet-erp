import { describe, expect, it } from 'vitest';
import type { CajaMovimientoDto, CajaSesionDetalleResponse } from '../api/types';
import { resumirSesionCaja } from './resumen-caja';

const sesion: CajaSesionDetalleResponse = {
  id: 'ses-1',
  cajaId: 'c-1',
  cajaNombre: 'Caja Mostrador Conkal',
  sucursalId: 's-1',
  responsableUsuarioId: 'u-test',
  estado: 'Abierta',
  diaOperacion: '2026-07-11',
  fechaApertura: '2026-07-11T14:00:00Z',
  fechaCierre: null,
  fondoApertura: 500,
  efectivoTeorico: null,
  efectivoDeclarado: null,
  diferencia: null,
  cierreExtemporaneo: false,
  notasCierre: null,
  autorizacionAperturaId: null,
  version: 1,
  cortes: [],
  movimientos: [
    {
      id: 'm-1',
      tipo: 'FondoApertura',
      formaPago: '01',
      importe: 500,
      moneda: 'MXN',
      descripcion: 'Fondo de apertura',
      referencia: null,
      cobroMostradorId: null,
      usuarioId: 'u-test',
      createdAt: '2026-07-11T14:00:00Z',
    },
  ],
  totalesPorForma: [{ formaPago: '01', montoSistema: 500, montoDeclarado: null }],
};

function movimiento(
  tipo: string,
  importe: number,
  formaPago = '01',
  cobroMostradorId: string | null = null,
): CajaMovimientoDto {
  return { ...sesion.movimientos[0], tipo, importe, formaPago, cobroMostradorId };
}

describe('resumirSesionCaja', () => {
  it('solo fondo', () => {
    expect(resumirSesionCaja(sesion)).toEqual({
      efectivo: 500,
      fondo: 500,
      cobrosEfectivo: 0,
      retiros: 0,
      depositos: 0,
      ajustes: 0,
      cobradoSesion: 0,
      numeroCobros: 0,
      otrasFormas: [],
    });
  });

  it('fondo, cobros mixtos y retiro; deduplica el mismo cobro entre formas', () => {
    expect(
      resumirSesionCaja({
        ...sesion,
        movimientos: [
          ...sesion.movimientos,
          movimiento('CobroCliente', 200, '01', 'c-1'),
          movimiento('CobroCliente', 300, '04', 'c-1'),
          movimiento('CobroCliente', 50),
          movimiento('CobroCliente', 25),
          movimiento('Retiro', -100),
        ],
        totalesPorForma: [
          { formaPago: '01', montoSistema: 675, montoDeclarado: null },
          { formaPago: '04', montoSistema: 300, montoDeclarado: null },
        ],
      }),
    ).toMatchObject({
      efectivo: 675,
      fondo: 500,
      cobrosEfectivo: 275,
      retiros: -100,
      cobradoSesion: 575,
      numeroCobros: 3,
      otrasFormas: [{ formaPago: '04', monto: 300 }],
    });
  });

  it('resta reversas sin contarlas como nuevos cobros', () => {
    expect(
      resumirSesionCaja({
        ...sesion,
        movimientos: [
          ...sesion.movimientos,
          movimiento('CobroCliente', 200, '01', 'c-1'),
          movimiento('ReversaCobro', -200, '01', 'c-1'),
          movimiento('ReversaCobro', -50, '04', 'c-2'),
        ],
      }),
    ).toMatchObject({ cobrosEfectivo: 0, cobradoSesion: -50, numeroCobros: 1 });
  });

  it('usa fondoApertura si falta el total de efectivo, sin recalcularlo', () => {
    expect(
      resumirSesionCaja({
        ...sesion,
        totalesPorForma: [],
        movimientos: [...sesion.movimientos, movimiento('CobroCliente', 200)],
      }).efectivo,
    ).toBe(500);
  });

  it('conserva un total cero y separa depósitos y ajustes de efectivo', () => {
    expect(
      resumirSesionCaja({
        ...sesion,
        totalesPorForma: [{ formaPago: '01', montoSistema: 0, montoDeclarado: null }],
        movimientos: [
          ...sesion.movimientos,
          movimiento('Deposito', 100),
          movimiento('AjusteCorreccion', -25),
          movimiento('Deposito', 900, '04'),
        ],
      }),
    ).toMatchObject({ efectivo: 0, depositos: 100, ajustes: -25, cobradoSesion: 0 });
  });

  it('en arqueo usa el corte, incluso si difiere del total', () => {
    expect(
      resumirSesionCaja({
        ...sesion,
        estado: 'EnArqueo',
        cortes: [{ formaPago: '01', montoSistema: 450, montoDeclarado: 400 }],
      }).efectivo,
    ).toBe(450);
    expect(resumirSesionCaja({ ...sesion, estado: 'EnArqueo' }).efectivo).toBe(0);
  });
});
