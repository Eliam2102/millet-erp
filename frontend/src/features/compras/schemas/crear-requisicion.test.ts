import { describe, expect, it } from 'vitest';
import { CrearRequisicionSchema } from '@/features/compras/schemas/crear-requisicion';
import {
  Clasificacion,
  Prioridad,
} from '@/features/compras/api/types';

describe('CrearRequisicionSchema', () => {
  const baseValido = {
    sucursalId: '11111111-1111-4111-8111-111111111111',
    departamentoId: '22222222-2222-4222-8222-222222222222',
    clasificacion: Clasificacion.Servicio,
    prioridad: Prioridad.Normal,
    fechaSolicitud: '2026-05-09T10:00:00Z',
  };

  it('parsea un comando válido sin opcionales', () => {
    const result = CrearRequisicionSchema.parse(baseValido);
    expect(result.sucursalId).toBe(baseValido.sucursalId);
    expect(result.requisitanteId).toBeUndefined();
  });

  it('rechaza UUID inválido en sucursalId', () => {
    expect(() =>
      CrearRequisicionSchema.parse({ ...baseValido, sucursalId: 'no-es-uuid' }),
    ).toThrow();
  });

  it('acepta GUIDs deterministas del seed backend (no-RFC v1-v8 pero shape válido)', () => {
    // El seed del backend usa Guids tipo "00000005-0001-0000-0000-000000000001"
    // que NO son UUIDs v1-v8 (tercer grupo `0000` sin version válida).
    // Zod v4 .uuid() los refusaría; nuestro regex laxo los acepta.
    const result = CrearRequisicionSchema.parse({
      ...baseValido,
      sucursalId: '00000005-0001-0000-0000-000000000001',
      departamentoId: '00000005-0002-0000-0000-000000000003',
    });
    expect(result.sucursalId).toBe('00000005-0001-0000-0000-000000000001');
  });

  it('rechaza clasificacion fuera del enum', () => {
    expect(() =>
      CrearRequisicionSchema.parse({ ...baseValido, clasificacion: 99 }),
    ).toThrow();
  });

  it('rechaza descripcion > 500 caracteres', () => {
    expect(() =>
      CrearRequisicionSchema.parse({
        ...baseValido,
        descripcion: 'a'.repeat(501),
      }),
    ).toThrow();
  });

  it('acepta descripcion = 500 (límite exacto)', () => {
    const result = CrearRequisicionSchema.parse({
      ...baseValido,
      descripcion: 'a'.repeat(500),
    });
    expect(result.descripcion?.length).toBe(500);
  });

  it('acepta requisitanteId opcional cuando se provee (delegación)', () => {
    const result = CrearRequisicionSchema.parse({
      ...baseValido,
      requisitanteId: '44444444-4444-4444-8444-444444444444',
    });
    expect(result.requisitanteId).toBeDefined();
  });

  it('acepta fechaEntregaDeseada en formato YYYY-MM-DD', () => {
    const result = CrearRequisicionSchema.parse({
      ...baseValido,
      fechaEntregaDeseada: '2026-05-15',
    });
    expect(result.fechaEntregaDeseada).toBe('2026-05-15');
  });

  it('rechaza fechaEntregaDeseada con formato inválido', () => {
    expect(() =>
      CrearRequisicionSchema.parse({
        ...baseValido,
        fechaEntregaDeseada: '15/05/2026',
      }),
    ).toThrow();
  });
});
