import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, refrescarPermisos } from '@/lib/auth/api-client';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * Regresión del Content-Type por defecto en <c>apiFetch</c>: solo los
 * bodies string (JSON serializado por el caller) deben recibir
 * <c>application/json</c>. Forzarlo también en <c>FormData</c> pisaba el
 * <c>multipart/form-data; boundary=…</c> que fija el browser y todos los
 * uploads (packing list, vale) morían con 415 en el servidor.
 */

const fetchMock = vi.fn<typeof fetch>(
  async () => new Response(null, { status: 200 }),
);

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockClear();
  useAuthStore.setState({ accessToken: 'test-token' });
});

afterEach(() => {
  vi.unstubAllGlobals();
  useAuthStore.setState({ accessToken: null });
});

function headersDeLaLlamada(): Headers {
  const init = fetchMock.mock.calls[0]![1] as RequestInit;
  return init.headers as Headers;
}

describe('apiFetch — Content-Type por defecto', () => {
  it('body string (JSON) → application/json', async () => {
    await apiFetch('/api/x', {
      method: 'POST',
      body: JSON.stringify({ a: 1 }),
    });

    expect(headersDeLaLlamada().get('Content-Type')).toBe('application/json');
  });

  it('body FormData → NO fija Content-Type (el browser pone el boundary)', async () => {
    const fd = new FormData();
    fd.append('archivo', new Blob(['x'], { type: 'application/pdf' }), 'a.pdf');

    await apiFetch('/api/x', { method: 'POST', body: fd });

    expect(headersDeLaLlamada().has('Content-Type')).toBe(false);
  });

  it('respeta un Content-Type explícito del caller', async () => {
    await apiFetch('/api/x', {
      method: 'POST',
      body: 'texto plano',
      headers: { 'Content-Type': 'text/plain' },
    });

    expect(headersDeLaLlamada().get('Content-Type')).toBe('text/plain');
  });

  it('inyecta Authorization desde el auth store', async () => {
    await apiFetch('/api/x');

    expect(headersDeLaLlamada().get('Authorization')).toBe(
      'Bearer test-token',
    );
  });
});

describe('apiFetch — permisos retirados (U1.0)', () => {
  const me = (permisos: string[]) =>
    new Response(JSON.stringify({ permisos }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });

  it('un 403 recarga /api/auth/me y actualiza los permisos del store', async () => {
    useAuthStore.setState({ permisos: ['a.leer', 'a.autorizar'] });
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
      .mockResolvedValueOnce(me(['a.leer']));

    const response = await apiFetch('/api/v1/x/autorizar', { method: 'POST' });
    await refrescarPermisos();

    expect(response.status).toBe(403);
    expect(String(fetchMock.mock.calls[1]![0])).toContain('/api/auth/me');
    expect(useAuthStore.getState().permisos).toEqual(['a.leer']);
  });

  it('varios 403 simultáneos hacen una sola recarga', async () => {
    fetchMock.mockImplementation(async (input) =>
      String(input).includes('/api/auth/me') ? me(['a.leer']) : new Response(null, { status: 403 }),
    );

    await Promise.all([apiFetch('/api/v1/x'), apiFetch('/api/v1/y'), apiFetch('/api/v1/z')]);
    await refrescarPermisos();

    const recargas = fetchMock.mock.calls.filter(([url]) => String(url).includes('/api/auth/me'));
    expect(recargas).toHaveLength(1);
    fetchMock.mockReset();
    fetchMock.mockImplementation(async () => new Response(null, { status: 200 }));
  });

  it('un 403 de un endpoint de auth no dispara recarga', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 403 }));

    await apiFetch('/api/auth/sesion', { method: 'POST' });

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
