import { describe, expect, it } from 'vitest';
import { ApiError } from '@/lib/api/error';
import { errorUsuario, problemaUsuario } from './error-usuario';

const fallo = (status: number, code?: string) => new ApiError({
  type: 'internal', status, title: 'HTTP SQL endpoint', detail: 'payload secreto', code,
}, status);

describe('presentación de errores del Sprint 04', () => {
  it.each([400, 401, 403, 409, 412, 422, 428, 500])('no publica detalles técnicos de un error %s', (status) => {
    const mensaje = errorUsuario(fallo(status), 'No se pudo registrar la recepción.');
    expect(mensaje).not.toMatch(/HTTP|SQL|endpoint|payload|secreto/);
    expect(mensaje).toMatch(/Revisa|Inicia|Pide|Recarga/);
    expect(problemaUsuario(fallo(status))).toEqual({
      type: 'about:blank', status, title: 'No se pudo cargar la información', detail: errorUsuario(fallo(status)),
    });
  });
  it('indica la evidencia que falta para rechazar material', () => {
    expect(errorUsuario(fallo(422, 'DICTAMEN_RECHAZO_SIN_EVIDENCIA'))).toBe('Falta la evidencia del material rechazado. Adjunta una foto o documento en la recepción para continuar.');
  });
  it('conserva la causa de clasificación fiscal y la acción para corregirla', () => {
    expect(errorUsuario(fallo(422, 'PROVEEDOR_REGIMEN_NO_APLICA'))).toContain('Quita el régimen');
  });
  it('nombra la incidencia que abrió el rechazo (U-4)', () => {
    const rechazo = new ApiError({
      type: 'x', status: 422, title: 'El artículo no está asignado a la ubicación elegida. Asígnalo primero.',
      detail: 'El artículo no está asignado a la ubicación elegida. Asígnalo primero.', code: 'ENTRADA_SIN_ASIGNACION',
      incidenciaId: '00000000-0000-0000-0000-000000000001', incidenciaNumero: 12,
    }, 422);
    expect(errorUsuario(rechazo)).toMatch(/Se abrió la incidencia INC-12 para darle seguimiento\.$/);
    expect(errorUsuario(fallo(422))).not.toContain('incidencia');
  });
  it('no expone excepciones de red', () => {
    expect(errorUsuario(new Error('secret endpoint'))).toContain('Revisa tu conexión');
    expect(errorUsuario(new Error('secret endpoint'))).not.toContain('secret');
  });
});

it('conserva la causa de negocio y el folio para resolver un bloqueo', () => {
  const error = new ApiError({ type: 'about:blank', status: 422, title: 'Regla de negocio', detail: 'El proveedor tiene la OC-MID2026-000412 pendiente.' }, 422);
  expect(errorUsuario(error)).toBe('El proveedor tiene la OC-MID2026-000412 pendiente. Revisa los datos y resuelve lo pendiente antes de continuar.');
});
it.each(['UnaClaseInterna falló', 'Falló PROVEEDOR_ERROR', 'El endpoint falló', 'Error en /app/archivo.cs'])('oculta el detalle interno: %s', (detail) => {
  const error = new ApiError({ type: 'about:blank', status: 422, title: 'Regla', detail }, 422);
  expect(errorUsuario(error)).not.toContain(detail);
});

it('conserva una instrucción de negocio completa y puntúa los detalles sin punto', () => {
  const crear = (detail: string) => new ApiError({ type: 'about:blank', status: 422, title: 'Regla', detail }, 422);
  expect(errorUsuario(crear('Adjunta la cotización para continuar.'))).toBe('Adjunta la cotización para continuar.');
  expect(errorUsuario(crear('El registro tiene pendientes'))).toBe('El registro tiene pendientes. Revisa los datos y resuelve lo pendiente antes de continuar.');
});
