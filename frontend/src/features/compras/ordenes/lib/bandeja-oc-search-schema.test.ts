import { describe, expect, it } from 'vitest';
import {
  BandejaOcSearchSchema,
  DEFAULT_BANDEJA_OC_SEARCH,
} from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';

describe('BandejaOcSearchSchema', () => {
  it('input vacío: aplica defaults page=1, pageSize=50', () => {
    expect(BandejaOcSearchSchema.parse({})).toEqual(DEFAULT_BANDEJA_OC_SEARCH);
  });

  it('coerce números desde strings (TanStack pasa search params como string)', () => {
    const r = BandejaOcSearchSchema.parse({ page: '3', pageSize: '100' });
    expect(r.page).toBe(3);
    expect(r.pageSize).toBe(100);
  });

  it('rechaza pageSize < 10 (mínimo) cayendo a default', () => {
    // Zod con `.min(10)` lanza error; pero como pageSize es optional con default,
    // el caller (TanStack Router) puede preferir filtrar inválidos al default.
    // Acá probamos que el schema sí valida (lanza ZodError).
    expect(() => BandejaOcSearchSchema.parse({ pageSize: 5 })).toThrow();
  });

  it('rechaza pageSize > 200 (máximo backend)', () => {
    expect(() => BandejaOcSearchSchema.parse({ pageSize: 500 })).toThrow();
  });

  it('valida los 7 estados de OC', () => {
    for (const estado of Object.values(EstadoOrdenCompra)) {
      const r = BandejaOcSearchSchema.parse({ estado });
      expect(r.estado).toBe(estado);
    }
  });

  it('rechaza estado fuera del enum', () => {
    expect(() => BandejaOcSearchSchema.parse({ estado: 99 })).toThrow();
  });

  it('valida los 3 sub-estados de Recepción', () => {
    for (const v of Object.values(SubEstadoRecepcion)) {
      expect(
        BandejaOcSearchSchema.parse({ subEstadoRecepcion: v }).subEstadoRecepcion,
      ).toBe(v);
    }
  });

  it('valida los 3 sub-estados de Facturación', () => {
    for (const v of Object.values(SubEstadoFacturacion)) {
      expect(
        BandejaOcSearchSchema.parse({ subEstadoFacturacion: v })
          .subEstadoFacturacion,
      ).toBe(v);
    }
  });

  it('valida los 3 sub-estados de Pago', () => {
    for (const v of Object.values(SubEstadoPago)) {
      expect(
        BandejaOcSearchSchema.parse({ subEstadoPago: v }).subEstadoPago,
      ).toBe(v);
    }
  });

  it('campos string opcionales aceptan strings no vacíos', () => {
    const r = BandejaOcSearchSchema.parse({
      proveedorId: 'p-1',
      compradorTitularId: 'u-1',
      fechaDesde: '2026-05-01T00:00:00Z',
      fechaHasta: '2026-05-31T23:59:59Z',
      q: 'PO-2026',
    });
    expect(r.proveedorId).toBe('p-1');
    expect(r.compradorTitularId).toBe('u-1');
    expect(r.fechaDesde).toBe('2026-05-01T00:00:00Z');
    expect(r.fechaHasta).toBe('2026-05-31T23:59:59Z');
    expect(r.q).toBe('PO-2026');
  });

  it('rechaza string vacío en proveedorId (min 1)', () => {
    expect(() =>
      BandejaOcSearchSchema.parse({ proveedorId: '' }),
    ).toThrow();
  });
});
