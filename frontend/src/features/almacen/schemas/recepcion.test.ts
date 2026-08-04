import { describe, expect, it } from 'vitest';
import { RegistrarRecepcionFacturaSchema } from './recepcion';

/**
 * Tests del refine de vínculo fiscal obligatorio en variante A (§5.4):
 * la recepción con factura exige cfdiRecibidoId O cfdiUuidFiscal.
 */

const filaIncluida = {
  lineaOcId: '019e0000-0000-7000-8000-000000000001',
  articuloId: '019e0000-0000-7000-8000-000000000002',
  posicion: 1,
  unidadMedida: 'PZA',
  cantidadSolicitada: 10,
  cantidadYaRecibida: 0,
  pendiente: 10,
  incluida: true,
  cantidad: 10,
  ubicacionId: '019e0000-0000-7000-8000-000000000003',
};

const base = {
  ordenCompraId: '019e0000-0000-7000-8000-0000000000aa',
  subAlmacenId: '019e0000-0000-7000-8000-0000000000bb',
  fechaMovimiento: '2026-07-14',
  observaciones: null,
  filas: [filaIncluida],
};

describe('RegistrarRecepcionFacturaSchema — vínculo fiscal obligatorio', () => {
  it('acepta con cfdiRecibidoId y sin folio fiscal', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      cfdiUuidFiscal: null,
    });
    expect(r.success).toBe(true);
  });

  it('acepta con folio fiscal (UUID SAT) y sin cfdiRecibidoId', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: null,
      cfdiUuidFiscal: 'AD662D33-6934-459C-A128-BDF0393E0062',
    });
    expect(r.success).toBe(true);
  });

  it('rechaza sin cfdiRecibidoId ni folio fiscal, con error en cfdiRecibidoId', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: null,
      cfdiUuidFiscal: null,
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(
        r.error.issues.some((i) => i.path.join('.') === 'cfdiRecibidoId'),
      ).toBe(true);
    }
  });

  it('rechaza folio fiscal malformado', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: null,
      cfdiUuidFiscal: 'no-es-un-uuid',
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(
        r.error.issues.some((i) => i.path.join('.') === 'cfdiUuidFiscal'),
      ).toBe(true);
    }
  });
});
