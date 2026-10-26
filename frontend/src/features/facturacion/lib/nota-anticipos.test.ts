import { describe, expect, it } from 'vitest';
import { notaAnticiposAplicados } from './nota-anticipos';

// 86e3tfdw2 — espejo de NotaAnticiposAplicados.Componer (backend).
describe('notaAnticiposAplicados', () => {
  it('compone folios e importes en formato es-MX', () => {
    expect(
      notaAnticiposAplicados([
        { folio: 'FANT-12', importe: 5000, moneda: 'MXN' },
        { folio: 'FANT-15', importe: 1200, moneda: 'MXN' },
      ]),
    ).toBe('Anticipos aplicados: FANT-12 $5,000.00 MXN; FANT-15 $1,200.00 MXN');
  });

  it('redondea a dos decimales (half away from zero) y respeta la moneda', () => {
    expect(notaAnticiposAplicados([{ folio: ' FANT-1 ', importe: 1234567.005, moneda: 'usd' }])).toBe(
      'Anticipos aplicados: FANT-1 $1,234,567.01 USD',
    );
  });

  it('sin anticipos devuelve cadena vacía', () => {
    expect(notaAnticiposAplicados([])).toBe('');
  });
});
