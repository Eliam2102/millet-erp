import { describe, expect, it } from 'vitest';
import { estadoRegularizacion } from './estado-regularizacion';

describe('regularización de vales', () => {
  it.each([
    [true, true, 'Vencido', 'danger'],
    [true, false, 'Por regularizar', 'warning'],
    [false, true, 'Regularizado', 'success'],
    [false, false, 'Regularizado', 'success'],
  ] as const)('pendiente %s, vencido %s', (pendienteRegularizacion, vencido, texto, variant) => {
    expect(estadoRegularizacion({ pendienteRegularizacion, vencido })).toEqual({ texto, variant });
  });
});
