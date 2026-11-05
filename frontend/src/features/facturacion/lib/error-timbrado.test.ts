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

it.each<[number, string, string | undefined]>([
  [500, 'System.InvalidOperationException', 'Detalle interno del servicio'],
  [422, 'FAC_PEDIDO_NO_EMBARCADO', undefined],
  [400, 'Error', 'HTTP 400: invalid payload at endpoint'],
])('oculta el error técnico %s con causa y acción de usuario', (status, title, detail) => {
  const error = new ApiError({ type: 'about:blank', status, title, detail }, status);
  expect(mensajeErrorTimbrado(error)).toBe('No se pudo completar el timbrado. Revisa el estado del comprobante con Facturación antes de reintentar.');
});
it('conserva la causa y la acción para la configuración fiscal faltante', () => {
  const error = new ApiError({ type: 'about:blank', status: 500, title: 'CONFIG_PAC_NO_DISPONIBLE', code: 'CONFIG_PAC_NO_DISPONIBLE' }, 500);
  expect(mensajeErrorTimbrado(error)).toBe('No hay timbrado configurado para esta empresa. Pide a un administrador que lo active en Administración → Integraciones fiscales.');
});

it('conserva un mensaje de negocio estructurado aunque el servicio responda 500', () => {
  const mensaje = 'No fue posible contactar al servicio fiscal.';
  const error = new ApiError({ type: 'about:blank', status: 500, title: 'No se pudo timbrar', detail: mensaje,
    errores: [{ campo: 'configuracionPac', codigo: 'FALLO_FISCAL', mensaje }],
  }, 500);
  expect(mensajeErrorTimbrado(error)).toBe(mensaje);
});
