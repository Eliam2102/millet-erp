import { describe, expect, it } from 'vitest';
import { aRegistrarCotizacionRequest, cotizacionSchema, type CotizacionValues } from './cotizacion';

const base: CotizacionValues = {
  proveedorId: '00000005-0001-0000-0000-000000000001',
  folioProveedor: '',
  moneda: 'MXN',
  fechaCotizacion: '2026-10-01',
  vigenteHasta: '2026-10-20',
  condicionesCredito: '',
  lineas: [
    {
      lineaRequisicionId: '00000007-0001-0000-0000-000000000001',
      unidadRequisicion: 'HOJA',
      unidadCotizada: 'M2',
      factorAUnidadRq: 0.4,
      precioUnitario: 10,
      diasEntrega: null,
    },
  ],
};

describe('cotizacionSchema', () => {
  it('acepta una cotización válida y el body no lleva unidadRequisicion', () => {
    const r = cotizacionSchema.safeParse(base);
    expect(r.success).toBe(true);
    const body = aRegistrarCotizacionRequest(base);
    expect(body.folioProveedor).toBeNull();
    expect(body.lineas[0]).not.toHaveProperty('unidadRequisicion');
  });

  it('exige factor 1 cuando la unidad cotizada es la de la RQ', () => {
    const r = cotizacionSchema.safeParse({
      ...base,
      lineas: [{ ...base.lineas[0]!, unidadCotizada: 'hoja', factorAUnidadRq: 12 }],
    });
    expect(r.success).toBe(false);
    expect(JSON.stringify(r.error?.issues)).toContain('el factor es 1');
  });

  it('rechaza vigencia anterior a la fecha y precio no positivo', () => {
    const r = cotizacionSchema.safeParse({
      ...base,
      vigenteHasta: '2026-09-30',
      lineas: [{ ...base.lineas[0]!, precioUnitario: 0 }],
    });
    expect(r.success).toBe(false);
    const msgs = JSON.stringify(r.error?.issues);
    expect(msgs).toContain('La vigencia no puede ser anterior');
    expect(msgs).toContain('El precio debe ser mayor a cero');
  });
});

it('conserva pago y entrega y rechaza una entrega anterior a la cotización', () => {
  const values = { ...base, condicionesPagoId: '00000002-0003-0000-0000-000000000003', fechaEntrega: '2026-10-15' };
  expect(aRegistrarCotizacionRequest(cotizacionSchema.parse(values))).toMatchObject({ condicionesPagoId: values.condicionesPagoId, fechaEntrega: values.fechaEntrega });
  expect(cotizacionSchema.safeParse({ ...values, fechaEntrega: '2026-09-30' }).success).toBe(false);
});
