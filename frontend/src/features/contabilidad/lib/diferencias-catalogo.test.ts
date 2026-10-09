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
  it('distingue alta, vacío y valores booleanos', () => {
    expect(diferenciasCatalogo({ antes: null, despues: antes }).every((d) => d.antes === undefined)).toBe(true);
    expect([true, false, null].map(valorDiferencia)).toEqual(['Sí', 'No', 'Sin valor']);
  });
});
