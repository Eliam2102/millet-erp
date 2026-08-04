import { describe, expect, it } from 'vitest';
import {
  EditarLineaCreditoSchema,
  NuevaLineaCreditoSchema,
} from '@/features/cxc/schemas/linea-credito';
import { OrigenLineaCredito } from '@/features/cxc/api/types';

const base = {
  clienteId: '018f6a5e-0000-7000-8000-000000000001',
  moneda: 'MXN' as const,
  limite: 500000,
  origen: OrigenLineaCredito.Solunion,
  plazoDias: 30,
  clasificacion: 'A' as const,
};

describe('NuevaLineaCreditoSchema', () => {
  it('acepta el caso válido completo', () => {
    expect(NuevaLineaCreditoSchema.parse(base)).toEqual(base);
  });

  it('acepta clasificación null (sin clasificar)', () => {
    const r = NuevaLineaCreditoSchema.parse({ ...base, clasificacion: null });
    expect(r.clasificacion).toBeNull();
  });

  it('rechaza clienteId no-GUID', () => {
    expect(
      NuevaLineaCreditoSchema.safeParse({ ...base, clienteId: 'x' }).success,
    ).toBe(false);
  });

  it('rechaza moneda fuera de MXN/USD', () => {
    expect(
      NuevaLineaCreditoSchema.safeParse({ ...base, moneda: 'EUR' }).success,
    ).toBe(false);
  });

  it('rechaza límite 0 o negativo', () => {
    expect(NuevaLineaCreditoSchema.safeParse({ ...base, limite: 0 }).success).toBe(false);
    expect(NuevaLineaCreditoSchema.safeParse({ ...base, limite: -5 }).success).toBe(false);
  });

  it('rechaza plazo 0 y > 365 (espejo del validator backend)', () => {
    expect(NuevaLineaCreditoSchema.safeParse({ ...base, plazoDias: 0 }).success).toBe(false);
    expect(NuevaLineaCreditoSchema.safeParse({ ...base, plazoDias: 366 }).success).toBe(false);
  });

  it('rechaza clasificación fuera de A/B/C/E', () => {
    expect(
      NuevaLineaCreditoSchema.safeParse({ ...base, clasificacion: 'D' }).success,
    ).toBe(false);
  });
});

describe('EditarLineaCreditoSchema', () => {
  it('solo pide límite/plazo/clasificación', () => {
    const r = EditarLineaCreditoSchema.parse({
      limite: 100,
      plazoDias: 15,
      clasificacion: null,
    });
    expect(r.limite).toBe(100);
  });

  it('rechaza plazo inválido', () => {
    expect(
      EditarLineaCreditoSchema.safeParse({
        limite: 100,
        plazoDias: 400,
        clasificacion: null,
      }).success,
    ).toBe(false);
  });
});
