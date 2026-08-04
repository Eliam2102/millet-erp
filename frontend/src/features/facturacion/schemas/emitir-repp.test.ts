import { describe, expect, it } from 'vitest';
import { EmitirReppSchema } from '@/features/facturacion/schemas/emitir-repp';

const valido = {
  sucursalId: '11111111-1111-4111-8111-111111111111',
  fechaPago: '2026-05-30',
  monedaPago: 'MXN',
  tcPago: null,
  formaPagoReal: '03',
  cuentaOrdenante: null,
  cuentaBeneficiaria: null,
  referenciaPago: null,
  facturas: [
    { facturaVentaId: '22222222-2222-4222-8222-222222222222', importePagado: 1000 },
  ],
};

describe('EmitirReppSchema', () => {
  it('acepta un REPP válido', () => {
    expect(EmitirReppSchema.safeParse(valido).success).toBe(true);
  });

  it('exige al menos una factura', () => {
    expect(EmitirReppSchema.safeParse({ ...valido, facturas: [] }).success).toBe(
      false,
    );
  });

  it('exige importe pagado > 0', () => {
    const r = EmitirReppSchema.safeParse({
      ...valido,
      facturas: [{ facturaVentaId: valido.facturas[0].facturaVentaId, importePagado: 0 }],
    });
    expect(r.success).toBe(false);
  });

  it('exige GUID de factura válido', () => {
    const r = EmitirReppSchema.safeParse({
      ...valido,
      facturas: [{ facturaVentaId: 'no-guid', importePagado: 100 }],
    });
    expect(r.success).toBe(false);
  });

  it('exige moneda de 3 caracteres', () => {
    expect(EmitirReppSchema.safeParse({ ...valido, monedaPago: 'PESO' }).success).toBe(
      false,
    );
  });
});
