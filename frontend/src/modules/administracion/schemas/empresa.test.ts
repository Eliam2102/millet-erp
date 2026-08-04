import { describe, expect, it } from 'vitest';
import {
  ActualizarEmpresaSchema,
  CrearEmpresaSchema,
} from '@/modules/administracion/schemas/empresa';

describe('CrearEmpresaSchema', () => {
  const valid = {
    rfc: 'MIL010101ABC',
    razonSocial: 'Millet S.A. de C.V.',
    regimenFiscal: '601',
    nombreComercial: 'Millet',
  };

  it('acepta un payload válido (12 chars RFC)', () => {
    const parsed = CrearEmpresaSchema.parse(valid);
    expect(parsed.rfc).toBe('MIL010101ABC');
    expect(parsed.razonSocial).toBe('Millet S.A. de C.V.');
  });

  it('acepta RFC de 13 caracteres (persona física)', () => {
    const result = CrearEmpresaSchema.safeParse({
      ...valid,
      rfc: 'ABCD800101XYZ',
    });
    expect(result.success).toBe(true);
  });

  it('uppercase-a el RFC ingresado en minúsculas', () => {
    const parsed = CrearEmpresaSchema.parse({ ...valid, rfc: 'mil010101abc' });
    expect(parsed.rfc).toBe('MIL010101ABC');
  });

  it('rechaza RFC de menos de 12 caracteres', () => {
    const result = CrearEmpresaSchema.safeParse({ ...valid, rfc: 'MIL01' });
    expect(result.success).toBe(false);
    if (!result.success) {
      const flat = result.error.flatten().fieldErrors;
      expect(flat.rfc?.[0]).toMatch(/12 o 13 caracteres/i);
    }
  });

  it('rechaza RFC de más de 13 caracteres', () => {
    const result = CrearEmpresaSchema.safeParse({
      ...valid,
      rfc: 'MIL010101ABCDE',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza RFC con caracteres inválidos (minúsculas no permitidas después de uppercase)', () => {
    // El schema hace .toUpperCase() antes de regex; un caracter no
    // alfanumérico (espacio) sí falla.
    const result = CrearEmpresaSchema.safeParse({
      ...valid,
      rfc: 'MIL 010101AB',
    });
    expect(result.success).toBe(false);
  });

  it('acepta & y Ñ en el RFC (válidos para personas morales)', () => {
    const result = CrearEmpresaSchema.safeParse({
      ...valid,
      rfc: 'MOÑ&20010101', // 12 chars exactos
    });
    expect(result.success).toBe(true);
  });

  it('rechaza razonSocial vacía', () => {
    const result = CrearEmpresaSchema.safeParse({ ...valid, razonSocial: '' });
    expect(result.success).toBe(false);
  });

  it('rechaza razonSocial > 254 chars', () => {
    const result = CrearEmpresaSchema.safeParse({
      ...valid,
      razonSocial: 'a'.repeat(255),
    });
    expect(result.success).toBe(false);
  });

  it('rechaza regimenFiscal > 10 chars', () => {
    const result = CrearEmpresaSchema.safeParse({
      ...valid,
      regimenFiscal: '12345678901',
    });
    expect(result.success).toBe(false);
  });

  it('normaliza nombreComercial vacío a null', () => {
    const parsed = CrearEmpresaSchema.parse({ ...valid, nombreComercial: '' });
    expect(parsed.nombreComercial).toBeNull();
  });

  it('acepta nombreComercial null directamente', () => {
    const parsed = CrearEmpresaSchema.parse({ ...valid, nombreComercial: null });
    expect(parsed.nombreComercial).toBeNull();
  });

  it('rechaza nombreComercial > 254 chars', () => {
    const result = CrearEmpresaSchema.safeParse({
      ...valid,
      nombreComercial: 'a'.repeat(255),
    });
    expect(result.success).toBe(false);
  });
});

describe('ActualizarEmpresaSchema', () => {
  it('acepta payload válido sin RFC', () => {
    const parsed = ActualizarEmpresaSchema.parse({
      razonSocial: 'Nueva razón',
      regimenFiscal: '603',
      nombreComercial: null,
      tasaIvaDefault: null,
      codigoPostal: null,
    });
    expect(parsed.razonSocial).toBe('Nueva razón');
  });

  it('aplica las mismas reglas de longitud que CrearEmpresaSchema', () => {
    const result = ActualizarEmpresaSchema.safeParse({
      razonSocial: '',
      regimenFiscal: '601',
      nombreComercial: null,
      tasaIvaDefault: null,
      codigoPostal: null,
    });
    expect(result.success).toBe(false);
  });

  it('valida codigoPostal de 5 dígitos con null permitido (F12-PR1)', () => {
    const base = {
      razonSocial: 'Millet SA',
      regimenFiscal: '601',
      nombreComercial: null,
      tasaIvaDefault: null,
    };
    expect(
      ActualizarEmpresaSchema.parse({ ...base, codigoPostal: '76120' })
        .codigoPostal,
    ).toBe('76120');
    expect(
      ActualizarEmpresaSchema.parse({ ...base, codigoPostal: null })
        .codigoPostal,
    ).toBeNull();
    expect(
      ActualizarEmpresaSchema.safeParse({ ...base, codigoPostal: '761' })
        .success,
    ).toBe(false);
    expect(
      ActualizarEmpresaSchema.safeParse({ ...base, codigoPostal: 'ABCDE' })
        .success,
    ).toBe(false);
  });

  it('valida el rango de tasaIvaDefault (fracción 0–1, FAC-DET-PR3)', () => {
    const base = {
      razonSocial: 'Millet SA',
      regimenFiscal: '601',
      nombreComercial: null,
      codigoPostal: null,
    };
    expect(
      ActualizarEmpresaSchema.safeParse({ ...base, tasaIvaDefault: 0.16 })
        .success,
    ).toBe(true);
    expect(
      ActualizarEmpresaSchema.safeParse({ ...base, tasaIvaDefault: 16 })
        .success,
    ).toBe(false);
    expect(
      ActualizarEmpresaSchema.safeParse({ ...base, tasaIvaDefault: -0.01 })
        .success,
    ).toBe(false);
  });
});
