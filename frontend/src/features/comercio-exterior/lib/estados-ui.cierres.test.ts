import { describe, expect, it } from 'vitest';
import { cierresPendientesDeAprobar } from './estados-ui';

const registrado = '2026-11-11T12:00:00Z';
describe('Aprobar cierre (Gerencia)', () => {
  it('no deja pendientes con los tres cierres registrados, aunque el financiero ya esté cerrado', () => {
    expect(
      cierresPendientesDeAprobar(
        ['fisico', 'documental', 'financiero'].map((dimension) => ({
          dimension,
          fecha: registrado,
          faltantes: [],
        })),
      ),
    ).toEqual([]);
  });
  it('lista los cierres sin registrar o con faltantes', () => {
    expect(
      cierresPendientesDeAprobar([
        { dimension: 'fisico', fecha: registrado, faltantes: [] },
        { dimension: 'documental', fecha: null, faltantes: [] },
        { dimension: 'financiero', fecha: registrado, faltantes: ['Gastos sin distribuir'] },
      ]),
    ).toEqual(['cierre documental', 'cierre financiero']);
  });
});
