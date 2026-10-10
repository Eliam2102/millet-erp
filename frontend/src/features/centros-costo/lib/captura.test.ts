import { describe, expect, it } from 'vitest';
import { centroCostoInicial, etiquetaNivelCentroCosto } from './captura';
import type { CentroCostoCaptura, CentroCostoOpcion } from '../api/captura';
const area: CentroCostoOpcion = { id: 'a', clave: 'A', nombre: 'DEMO área', nivel: 2, activo: true, dim1Id: 'p', dim2Id: 'a' };
const maquina = { ...area, id: 'm', nivel: 3 };
const ctx: CentroCostoCaptura = { heredado: area, puedeElegir: false, unicaOpcion: null, mensaje: null };
describe('ADM08: prellenado', () => {
  it('hereda el departamento sin máquina y sin alcance', () => expect(centroCostoInicial(ctx)).toEqual(area));
  it('prioriza el departamento aun con alcance para una máquina', () => expect(centroCostoInicial({ ...ctx, puedeElegir: true, unicaOpcion: maquina })).toEqual(area));
  it('prellena la única opción cuando falta equivalencia', () => expect(centroCostoInicial({ ...ctx, heredado: null, puedeElegir: true, unicaOpcion: maquina })).toEqual(maquina));
  it('sin equivalencia ni alcance queda vacío', () => expect(centroCostoInicial({ ...ctx, heredado: null })).toBeNull());
  it('la etiqueta explica que máquina es opcional', () => expect(etiquetaNivelCentroCosto(3)).toBe('Máquina (opcional)'));
});
