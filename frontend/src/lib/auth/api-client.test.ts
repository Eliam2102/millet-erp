import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch } from '@/lib/auth/api-client';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * Regresión del Content-Type por defecto en <c>apiFetch</c>: solo los
 * bodies string (JSON serializado por el caller) deben recibir
 * <c>application/json</c>. Forzarlo también en <c>FormData</c> pisaba el
 * <c>multipart/form-data; boundary=…</c> que fija el browser y todos los
 * uploads (packing list, vale) morían con 415 en el servidor.
 */

const fetchMock = vi.fn(
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
