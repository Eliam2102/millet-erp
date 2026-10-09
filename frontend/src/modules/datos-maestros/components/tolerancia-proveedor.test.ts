import { describe, expect, it } from 'vitest';
import { interpretarToleranciaMxn } from './tolerancia-proveedor';

describe('Tolerancia del proveedor en MXN', () => {
  it.each([['', null], ['  ', null], ['0', 0], ['5', 5], ['0.99', 0.99], ['1.50', 1.5], ['0.0001', 0.0001]] as const)(
    'interpreta %s como %s, conservando cero y ausencia distintos', (texto, monto) => {
      expect(interpretarToleranciaMxn(texto)).toEqual({ valido: true, monto });
    },
  );
  it.each(['-1', '1%', '1,50', '1,500', 'NaN', 'Infinity', '1e3', '0x10', 'abc', '0.00001', '100000000000000'])(
    'rechaza %s', (texto) => expect(interpretarToleranciaMxn(texto)).toEqual({ valido: false }),
  );
});
