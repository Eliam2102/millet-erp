import { describe, expect, it } from 'vitest';
import {
  TerminarRequisicionSchema,
  buildTerminarSchema,
} from '@/features/compras/schemas/terminar-requisicion';

const MOTIVO_VALIDO = '11111111-1111-4111-8111-111111111111';

describe('TerminarRequisicionSchema (base)', () => {
  it('acepta motivoId UUID válido sin texto', () => {
    const result = TerminarRequisicionSchema.parse({
      motivoId: MOTIVO_VALIDO,
      motivoTexto: null,
    });
    expect(result.motivoId).toBe(MOTIVO_VALIDO);
    expect(result.motivoTexto).toBeNull();
  });

  it('acepta motivoId con texto opcional', () => {
    const result = TerminarRequisicionSchema.parse({
      motivoId: MOTIVO_VALIDO,
      motivoTexto: 'Detalle voluntario',
    });
    expect(result.motivoTexto).toBe('Detalle voluntario');
  });

  it('acepta motivoId con shape laxo (no v1-v8)', () => {
    // Backend usa GUIDs deterministas en seeds (00000005-0001-0000-...).
    const result = TerminarRequisicionSchema.parse({
      motivoId: '00000005-0001-0000-0000-000000000001',
      motivoTexto: null,
    });
    expect(result.motivoId).toBe('00000005-0001-0000-0000-000000000001');
  });

  it('rechaza motivoId vacío', () => {
    expect(() =>
      TerminarRequisicionSchema.parse({ motivoId: '', motivoTexto: null }),
    ).toThrow();
  });

  it('rechaza motivoId con shape inválido', () => {
    expect(() =>
      TerminarRequisicionSchema.parse({
        motivoId: 'not-a-uuid',
        motivoTexto: null,
      }),
    ).toThrow();
  });

  it('rechaza motivoTexto > 500 caracteres', () => {
    expect(() =>
      TerminarRequisicionSchema.parse({
        motivoId: MOTIVO_VALIDO,
        motivoTexto: 'a'.repeat(501),
      }),
    ).toThrow();
  });
});

describe('buildTerminarSchema(textoRequerido)', () => {
  it('con textoRequerido=false equivale al schema base (texto opcional)', () => {
    const schema = buildTerminarSchema(false);
    const result = schema.parse({ motivoId: MOTIVO_VALIDO });
    expect(result.motivoId).toBe(MOTIVO_VALIDO);
  });

  it('con textoRequerido=true exige texto no vacío', () => {
    const schema = buildTerminarSchema(true);
    expect(() =>
      schema.parse({ motivoId: MOTIVO_VALIDO, motivoTexto: '' }),
    ).toThrow();
    expect(() =>
      schema.parse({ motivoId: MOTIVO_VALIDO, motivoTexto: null }),
    ).toThrow();
  });

  it('con textoRequerido=true acepta texto válido', () => {
    const schema = buildTerminarSchema(true);
    const result = schema.parse({
      motivoId: MOTIVO_VALIDO,
      motivoTexto: 'Razón obligatoria',
    });
    expect(result.motivoTexto).toBe('Razón obligatoria');
  });

  it('con textoRequerido=true sigue capando a 500 caracteres', () => {
    const schema = buildTerminarSchema(true);
    expect(() =>
      schema.parse({
        motivoId: MOTIVO_VALIDO,
        motivoTexto: 'a'.repeat(501),
      }),
    ).toThrow();
  });
});
