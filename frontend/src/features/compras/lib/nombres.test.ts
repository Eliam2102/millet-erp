import { describe, expect, it } from 'vitest';
import { departamentoLabel, requisitanteLabel } from './nombres';

describe('nombres de requisiciones', () => {
  it('usa el nombre y la clave resueltos por el servidor', () => {
    expect(requisitanteLabel({ requisitanteId: 'id-interno', requisitanteNombre: 'Ana López' })).toBe('Ana López');
    expect(departamentoLabel({ departamentoId: 'id-interno', departamentoClave: 'COM', departamentoNombre: 'Compras' })).toBe('COM · Compras');
  });
  it.each([null, '', ' '])('no muestra IDs si falta el nombre (%s)', (nombre) => {
    expect(requisitanteLabel({ requisitanteId: 'id-interno', requisitanteNombre: nombre })).toBe('[REQUISITANTE POR CONFIRMAR]');
    expect(departamentoLabel({ departamentoId: 'id-interno', departamentoClave: null, departamentoNombre: nombre })).toBe('[DEPARTAMENTO POR CONFIRMAR]');
  });
});
