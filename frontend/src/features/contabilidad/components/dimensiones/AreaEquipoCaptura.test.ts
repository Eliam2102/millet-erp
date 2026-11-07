import { describe, expect, it } from 'vitest';
import type { CentroOpcion } from '../../api/dimensiones-types';
import { areaEquipoIds, faltaAreaEquipo } from './AreaEquipoCaptura';

const area: CentroOpcion = {
  id: 'area-1',
  nivel: 'Dim2',
  clave: 'A01',
  nombre: 'Área FIX',
  dim1Id: 'ubi-1',
  dim2Id: 'area-1',
  dim1Clave: 'U01',
  dim2Clave: 'A01',
};
const equipo: CentroOpcion = { ...area, id: 'eq-1', nivel: 'Dim3', clave: 'E01', nombre: 'Equipo FIX' };

describe('H1 · área y equipo capturados', () => {
  it('falta el área si la cuenta la exige y no hay área ni equipo', () => {
    expect(faltaAreaEquipo({ area: null, equipo: null }, true, false)).toBe(true);
    expect(faltaAreaEquipo({ area, equipo: null }, true, false)).toBe(false);
    expect(faltaAreaEquipo({ area: null, equipo }, true, false)).toBe(false);
  });

  it('una cuenta 600 también exige el equipo', () => {
    expect(faltaAreaEquipo({ area, equipo: null }, true, true)).toBe(true);
    expect(faltaAreaEquipo({ area, equipo }, true, true)).toBe(false);
  });

  it('sin exigencia nunca falta nada', () => {
    expect(faltaAreaEquipo({ area: null, equipo: null }, false, false)).toBe(false);
  });

  it('manda el área elegida o, si solo hay equipo, su área', () => {
    expect(areaEquipoIds({ area, equipo: null })).toEqual({ areaId: 'area-1', equipoId: undefined });
    expect(areaEquipoIds({ area: null, equipo })).toEqual({ areaId: 'area-1', equipoId: 'eq-1' });
    expect(areaEquipoIds({ area: null, equipo: null })).toEqual({ areaId: undefined, equipoId: undefined });
  });
});
