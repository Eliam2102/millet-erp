import { describe, expect, it } from 'vitest';
import { nombreUsuarioOGuion } from './nombre-usuario';

describe('nombreUsuarioOGuion', () => {
  it('devuelve el nombre cuando se conoce', () => {
    expect(nombreUsuarioOGuion('Ana López')).toBe('Ana López');
  });

  it('devuelve guion em cuando el nombre es nulo, vacío o solo espacios', () => {
    expect(nombreUsuarioOGuion(null)).toBe('—');
    expect(nombreUsuarioOGuion(undefined)).toBe('—');
    expect(nombreUsuarioOGuion('   ')).toBe('—');
  });
});
