import { describe, expect, it } from 'vitest';
import { bloqueoRelacion, totalRelacion, RevisionReppSchema } from './repp-pendiente';
const id = '00000000-0000-0000-0000-000000000001';
const otro = '00000000-0000-0000-0000-000000000002';
describe('Revisión de REP', () => {
  it('usa zId y acepta ids del seed sin versión RFC', () => {
    expect(
      RevisionReppSchema.safeParse({
        formaPago: '03',
        facturas: [{ facturaVentaId: id, importe: 1 }],
      }).success,
    ).toBe(true);
  });
  it('suma decimales sin error binario', () => {
    const facturas = [
      { facturaVentaId: id, importe: 0.1 },
      { facturaVentaId: otro, importe: 0.2 },
    ];
    expect(totalRelacion(facturas)).toBe(0.3);
    expect(bloqueoRelacion(0.3, '03', facturas)).toBeNull();
  });
  it('bloquea suma diferente, duplicados, negativos, vacíos y forma 99', () => {
    expect(bloqueoRelacion(100, '03', [{ facturaVentaId: id, importe: 99 }])).toMatch(
      /exactamente/,
    );
    expect(
      bloqueoRelacion(100, '03', [
        { facturaVentaId: id, importe: 50 },
        { facturaVentaId: id, importe: 50 },
      ]),
    ).toMatch(/repitas/);
    expect(bloqueoRelacion(100, '03', [{ facturaVentaId: id, importe: -1 }])).not.toBeNull();
    expect(bloqueoRelacion(100, '03', [])).not.toBeNull();
    expect(bloqueoRelacion(100, '99', [{ facturaVentaId: id, importe: 100 }])).not.toBeNull();
  });
});
