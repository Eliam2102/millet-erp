import { describe, expect, it } from 'vitest';
import { detectarEncabezado } from './archivo';
import { ALIAS_NOMINA, filaReal, motivoImportar } from './nomina';

describe('póliza de nómina externa (K10.11)', () => {
  it('reconoce el encabezado del .xlsx aunque traiga filas de título y alias de SAP', () => {
    const matriz = [
      ['Póliza de nómina Q22', '', ''],
      ['', '', ''],
      ['Cuenta', 'Debe', 'Haber', 'Sucursal', 'Centro de costo'],
      ['6101-001', '100', '', 'MATRIZ', ''],
    ];
    expect(detectarEncabezado(matriz, ALIAS_NOMINA)).toBe(3);
  });

  it('la fila del error se ajusta a la fila real del archivo', () => {
    expect(filaReal({ fila: 3, campo: 'cuenta', codigo: 'X', mensaje: '' }, 2)).toBe(5);
    expect(filaReal({ fila: null, campo: 'importes', codigo: 'NOM_NO_CUADRA', mensaje: '' }, 2)).toBeNull();
  });

  it('importar exige archivo, encabezado completo y una vista previa sin errores', () => {
    const base = { tipoOperacionId: 't', fecha: '2026-11-15', referencia: '2026-Q22', archivo: true, vistaValida: true };
    expect(motivoImportar(base)).toBeUndefined();
    expect(motivoImportar({ ...base, archivo: false })).toMatch(/archivo/);
    expect(motivoImportar({ ...base, referencia: ' ' })).toMatch(/referencia/);
    expect(motivoImportar({ ...base, vistaValida: null })).toMatch(/Revisa/);
    expect(motivoImportar({ ...base, vistaValida: false })).toMatch(/Corrige/);
  });
});
