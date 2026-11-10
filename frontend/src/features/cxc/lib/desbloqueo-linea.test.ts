import { describe, expect, it } from 'vitest';
import { EstadoLineaCredito } from '@/features/cxc/api/types';
import { desbloqueoManual } from './desbloqueo-linea';

describe('desbloqueoManual (B1)', () => {
  it('la línea desbloqueada a mano muestra cuándo y por qué', () => {
    expect(
      desbloqueoManual({
        estado: EstadoLineaCredito.Activa,
        desbloqueoManualEn: '2026-11-07T18:30:00Z',
        motivoDesbloqueo: ' Pago comprometido por el director ',
      }),
    ).toEqual({ en: '2026-11-07T18:30:00Z', motivo: 'Pago comprometido por el director' });
  });

  it('bloqueada otra vez o sin desbloqueo manual no muestra nada', () => {
    expect(
      desbloqueoManual({ estado: EstadoLineaCredito.Bloqueada, desbloqueoManualEn: '2026-11-07T18:30:00Z', motivoDesbloqueo: 'x' }),
    ).toBeNull();
    expect(desbloqueoManual({ estado: EstadoLineaCredito.Activa, desbloqueoManualEn: null, motivoDesbloqueo: null })).toBeNull();
    expect(desbloqueoManual({ estado: EstadoLineaCredito.Activa })).toBeNull();
  });
});
