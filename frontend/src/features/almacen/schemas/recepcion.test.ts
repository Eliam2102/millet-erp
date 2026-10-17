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
  toleranciaPorcentaje: 0,
  maximo: 10,
  incluida: true,
  cantidad: 10,
  cantidadDanada: 0,
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

  it('rechaza cantidad sobre el máximo del servidor con tolerancia 0 %', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      filas: [
        { ...filaIncluida, pendiente: 100, maximo: 100, cantidad: 101, toleranciaPorcentaje: 0 },
      ],
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues[0].message).toBe(
        'Excede el máximo recibible (100): pendiente 100 más 0% de tolerancia del artículo sobre lo solicitado.',
      );
    }
  });

  it.each([
    [110, true],
    [110.5, false],
  ])('con tolerancia 10 %% el máximo es 110: cantidad %s → ok=%s', (cantidad, ok) => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      filas: [
        { ...filaIncluida, pendiente: 100, maximo: 110, cantidad, toleranciaPorcentaje: 10 },
      ],
    });
    expect(r.success).toBe(ok);
  });

  it('acepta cantidad igual al máximo del servidor con tolerancia 5 %', () => {
    const r = RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      filas: [
        { ...filaIncluida, pendiente: 100, maximo: 105, cantidad: 105, toleranciaPorcentaje: 5 },
      ],
    });
    expect(r.success).toBe(true);
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

describe('RegistrarRecepcionFacturaSchema — cantidad dañada (ALM-04)', () => {
  const conFila = (extra: object) =>
    RegistrarRecepcionFacturaSchema.safeParse({
      ...base,
      cfdiRecibidoId: '019e0000-0000-7000-8000-0000000000cc',
      filas: [{ ...filaIncluida, ...extra }],
    });

  it('rechaza dañada mayor que la cantidad recibida', () => {
    const r = conFila({ cantidadDanada: 11, motivoDano: 'Rota' });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.map((i) => i.message)).toContain(
        'La cantidad dañada no puede ser mayor que la cantidad recibida.',
      );
    }
  });

  it('rechaza dañada sin motivo', () => {
    const r = conFila({ cantidadDanada: 2, motivoDano: '  ' });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.map((i) => i.message)).toContain('Indica el motivo del daño.');
    }
  });

  it('acepta dañada cero sin motivo y dañada con motivo', () => {
    expect(conFila({ cantidadDanada: 0 }).success).toBe(true);
    expect(conFila({ cantidadDanada: 2, motivoDano: 'Hojas rotas' }).success).toBe(true);
  });
});
