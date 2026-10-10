import { describe, expect, it } from 'vitest';
import { ApiError } from '@/lib/api';
import { mensajeErrorTimbrado } from './error-timbrado';

describe('mensajeErrorTimbrado', () => {
  it('conserva el detail, usa título si falta y ofrece una salida ante fallos de red', () => {
    expect(mensajeErrorTimbrado(new ApiError({ type: 'about:blank', title: 'Error', status: 422, detail: 'Corrige el importe.' }, 422)))
      .toBe('Corrige el importe.');
    expect(mensajeErrorTimbrado(new ApiError({ type: 'about:blank', title: 'Corrige los datos.', status: 422, detail: ' ' }, 422)))
      .toBe('Corrige los datos.');
    expect(mensajeErrorTimbrado(new Error('Network error'))).toContain('Revisa tu conexión');
  });
});
