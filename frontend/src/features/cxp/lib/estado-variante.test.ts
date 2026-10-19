import { describe, expect, it } from 'vitest';
import {
  EstadoAnticipo,
  EstadoComprobacionGastos,
  EstadoCuentaTcStatus,
  EstadoMovimientoTc,
  EstadoNotaCargo,
  EstadoNotaCredito,
  EstadoPasivo,
  EstadoSolicitudViaticos,
  EstadoTarjeta,
} from '@/features/cxp/api/types';
import {
  ESTADO_ANTICIPO,
  ESTADO_COMPROBACION,
  ESTADO_CUENTA_TC,
  ESTADO_MOVIMIENTO_TC,
  ESTADO_NOTA_CARGO,
  ESTADO_NOTA_CREDITO,
  ESTADO_PASIVO,
  ESTADO_SOLICITUD_VIATICOS,
  ESTADO_TARJETA,
} from './estado-variante';

describe('estado-variante CxP', () => {
  it.each([
    ['pasivo', EstadoPasivo, ESTADO_PASIVO],
    ['nota de crédito', EstadoNotaCredito, ESTADO_NOTA_CREDITO],
    ['anticipo', EstadoAnticipo, ESTADO_ANTICIPO],
    ['nota de cargo', EstadoNotaCargo, ESTADO_NOTA_CARGO],
    ['comprobación', EstadoComprobacionGastos, ESTADO_COMPROBACION],
    ['viáticos', EstadoSolicitudViaticos, ESTADO_SOLICITUD_VIATICOS],
    ['tarjeta', EstadoTarjeta, ESTADO_TARJETA],
    ['movimiento TC', EstadoMovimientoTc, ESTADO_MOVIMIENTO_TC],
    ['cuenta TC', EstadoCuentaTcStatus, ESTADO_CUENTA_TC],
  ] as const)('%s tiene entrada para cada valor del enum', (_n, enumObj, mapa) => {
    for (const v of Object.values(enumObj)) {
      const e = (mapa as Record<number, { etiqueta: string; variante: string }>)[v];
      expect(e?.etiqueta).toBeTruthy();
      expect(e?.variante).toBeTruthy();
    }
  });

  it('colores DS: Capturada neutral, Pagada success, Cancelada neutral', () => {
    expect(ESTADO_PASIVO[EstadoPasivo.Capturada].variante).toBe('neutral');
    expect(ESTADO_PASIVO[EstadoPasivo.Pagada].variante).toBe('success');
    expect(ESTADO_PASIVO[EstadoPasivo.Cancelada].variante).toBe('neutral');
  });
});
