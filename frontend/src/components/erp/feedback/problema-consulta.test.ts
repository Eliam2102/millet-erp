import { describe, expect, it } from 'vitest';
import { ApiError } from '@/lib/api';
import { problemaConsulta } from './problema-consulta';

describe('problemaConsulta', () => {
  it('conserva el 404 y oculta claves y excepciones del servidor', () => {
    const error = new ApiError({ type: 'about:blank', title: 'ENTITY_MISSING', detail: 'Invalid payload at endpoint', status: 404 }, 404);
    expect(problemaConsulta(error)).toEqual({
      type: 'about:blank', status: 404,
      title: 'No se pudo completar la consulta. Revisa tu conexión y vuelve a cargar los datos.',
    });
  });
  it('explica cómo resolver un permiso faltante sin exponer el permiso canónico', () => {
    const error = new ApiError({ type: 'about:blank', title: 'cuentas_por_cobrar.leer', status: 403 }, 403);
    expect(problemaConsulta(error)?.title).toBe('No tienes acceso a estos datos. Pide al administrador que revise tus permisos.');
  });
  it('da una acción ante un error de conexión sin mostrar la excepción', () => {
    expect(problemaConsulta(new Error('fetch failed'))).toBeUndefined();
  });
});
