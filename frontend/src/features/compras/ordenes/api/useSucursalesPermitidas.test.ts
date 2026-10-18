import { describe, expect, it } from 'vitest';
import { sucursalPorDefecto, type SucursalPermitida } from './useSucursalesPermitidas';

const SUC_A: SucursalPermitida = { id: '00000003-0002-0000-0000-000000000001', clave: 'MTY', nombre: 'Monterrey' };
const SUC_B: SucursalPermitida = { id: '00000003-0002-0000-0000-000000000002', clave: 'SLP', nombre: 'San Luis' };

describe('sucursalPorDefecto', () => {
  it('con una sola sucursal permitida la devuelve como default', () => {
    expect(sucursalPorDefecto([SUC_A])).toBe(SUC_A.id);
  });

  it('con varias sucursales no preselecciona (queda obligatoria)', () => {
    expect(sucursalPorDefecto([SUC_A, SUC_B])).toBeNull();
  });

  it('sin sucursales permitidas no preselecciona', () => {
    expect(sucursalPorDefecto([])).toBeNull();
  });
});
