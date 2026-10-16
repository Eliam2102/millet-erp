import { describe, expect, it } from 'vitest';
import { ApiError } from '@/lib/api/error';
import { mensajeErrorTransmision } from './mensajes-transmision';

const problema = (code: string, detail = 'Detalle del servidor') =>
  new ApiError({ type: 'business', title: 'Regla de negocio', status: 422, code, detail }, 422);

describe('mensajeErrorTransmision', () => {
  it('traduce IMPORT_INCOTERM_REQUERIDO a un mensaje legible', () => {
    expect(mensajeErrorTransmision(problema('IMPORT_INCOTERM_REQUERIDO'))).toMatch(/incoterm/i);
  });

  it('traduce IMPORT_PAIS_REQUERIDO a un mensaje legible', () => {
    expect(mensajeErrorTransmision(problema('IMPORT_PAIS_REQUERIDO'))).toMatch(/país de origen/i);
  });

  it('traduce OC_FICHA_TECNICA_REQUERIDA a un mensaje legible', () => {
    expect(mensajeErrorTransmision(problema('OC_FICHA_TECNICA_REQUERIDA'))).toBe(
      'Adjunta la ficha técnica de la orden antes de enviarla a autorización.',
    );
  });

  it('para otros códigos devuelve el detail del servidor', () => {
    expect(mensajeErrorTransmision(problema('TRANSMITIR_SIN_LINEAS'))).toBe('Detalle del servidor');
  });

  it('para errores que no son ApiError usa el message del Error', () => {
    expect(mensajeErrorTransmision(new Error('red caída'))).toBe('red caída');
  });
});
