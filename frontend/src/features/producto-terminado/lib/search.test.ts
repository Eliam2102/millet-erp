import { describe, expect, it } from 'vitest';
import { ptSearchSchema } from './search';
import { rutaPermitida } from '@/lib/nav';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
describe('Filtros y acceso PT', () => {
  it('acepta GUID sembrado y limpia páginas y filtros inválidos', () => {
    expect(
      ptSearchSchema.parse({ sucursalId: '00000003-0000-0000-0000-000000000001', page: '2' }).page,
    ).toBe(2);
    expect(ptSearchSchema.parse({ sucursalId: 'invalid', page: -1 })).toEqual({
      page: 1,
      sucursalId: undefined,
    });
  });
  it('cada bandeja exige su permiso propio', () => {
    expect(rutaPermitida('/producto-terminado/piezas', [])).toBe(false);
    expect(rutaPermitida('/producto-terminado/piezas', [P.ProductoTerminadoPiezasVer])).toBe(true);
    expect(rutaPermitida('/producto-terminado/excepciones', [P.ProductoTerminadoPiezasVer])).toBe(
      false,
    );
    expect(
      rutaPermitida('/producto-terminado/excepciones', [P.ProductoTerminadoExcepcionesGestionar]),
    ).toBe(true);
  });
});
