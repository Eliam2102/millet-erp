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

  it('OC_PRECIO_REQUERIDO muestra el mensaje del servidor que nombra las partidas', () => {
    const detail = 'Captura el precio de las partidas 1, 3 antes de enviar a autorización.';
    expect(mensajeErrorTransmision(problema('OC_PRECIO_REQUERIDO', detail))).toBe(detail);
  });

  it('OC_PRECIO_REQUERIDO sin detail usa el mensaje de respaldo', () => {
    expect(mensajeErrorTransmision(problema('OC_PRECIO_REQUERIDO', ''))).toBe(
      'Captura el precio de las partidas sin precio antes de enviar a autorización.',
    );
  });

  it('traduce la falta de partidas con una acción concreta', () => {
    expect(mensajeErrorTransmision(problema('TRANSMITIR_SIN_LINEAS'))).toBe('Agrega al menos una partida antes de enviar la orden a autorización.');
  });

  it('los errores inesperados no publican la excepción', () => {
    expect(mensajeErrorTransmision(new Error('red caída'))).toBe('No se pudo completar la acción. Revisa tu conexión y vuelve a cargar el registro; si continúa, avisa a soporte.');
  });
});

it('conserva los nombres de los datos fiscales que faltan al enviar', () => {
  const detail = 'Captura uso de CFDI, método de pago en los datos para la factura del proveedor antes de enviar a autorización.';
  expect(mensajeErrorTransmision(problema('OC_DATOS_FACTURA_INCOMPLETOS', detail))).toBe(detail);
});

it('un error técnico al pedir precios no se publica como motivo de negocio', () => {
  const mensaje = mensajeErrorTransmision(problema('OC_PRECIO_REQUERIDO', 'payload SQL endpoint'));
  expect(mensaje).toContain('Captura el precio de las partidas sin precio');
  expect(mensaje).not.toMatch(/payload|SQL|endpoint/);
});
