import { describe, expect, it } from 'vitest';
import type { Cuenta } from '../api/types';
import { diferenciasCatalogo, valorDiferencia } from './diferencias-catalogo';

const antes: Cuenta = {
  id: '019a0000-0000-7000-8000-000000000001', codigo: 'FIX-1', nombre: 'FIX', padreId: null,
  nivel: 1, naturaleza: 'Deudora', tipo: 'Titulo', estatus: 'Activo', activa: true, cuentaControl: 'Ninguna',
  noAfectableManual: false, codigoAgrupador: null, grupoReporte: null, pendienteValidacion: false,
  version: 1, clase: 'Cuenta', rubroId: null,
};
describe('diferencias del catálogo', () => {
  it('muestra solo datos de negocio que cambiaron, incluida la casilla', () => {
    const despues = { ...antes, nombre: 'FIX corregido', noAfectableManual: true, version: 2 };
    expect(diferenciasCatalogo({ antes, despues })).toEqual([
      { campo: 'nombre', etiqueta: 'Nombre', antes: 'FIX', despues: 'FIX corregido' },
      { campo: 'noAfectableManual', etiqueta: 'No afectable por asiento manual', antes: false, despues: true },
    ]);
  });
  it('muestra el código de la cuenta padre y del rubro en lugar de su id; compara por id', () => {
    const padre = '019a0000-0000-7000-8000-0000000000aa';
    const rubro = '019a0000-0000-7000-8000-0000000000bb';
    const despues = { ...antes, padreId: padre, rubroId: rubro, version: 2 };
    const codigos = { [padre]: 'FIX-PADRE', [rubro]: 'FIX-RUBRO' };
    expect(diferenciasCatalogo({ antes, despues }, codigos)).toEqual([
      { campo: 'padreId', etiqueta: 'Cuenta padre', antes: null, despues: 'FIX-PADRE' },
      { campo: 'rubroId', etiqueta: 'Rubro', antes: null, despues: 'FIX-RUBRO' },
    ]);
    // Sin código conocido (respuesta anterior del API) conserva el valor original.
    expect(diferenciasCatalogo({ antes, despues })[0].despues).toBe(padre);
    // Mismo id antes y después: no es una diferencia aunque el código no venga.
    expect(diferenciasCatalogo({ antes: despues, despues: { ...despues, nombre: 'otro' } }, codigos).map((d) => d.campo)).toEqual(['nombre']);
  });
  it('distingue alta, vacío y valores booleanos', () => {
    expect(diferenciasCatalogo({ antes: null, despues: antes }).every((d) => d.antes === undefined)).toBe(true);
    expect([true, false, null].map(valorDiferencia)).toEqual(['Sí', 'No', 'Sin valor']);
  });
});
