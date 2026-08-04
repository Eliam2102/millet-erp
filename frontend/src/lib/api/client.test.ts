import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, MAX_IDEMPOTENCY_RETRIES } from '@/lib/api/client';
import {
  esConflictoConcurrencia,
  esIdempotencyEnCurso,
} from '@/lib/api/error';

vi.mock('@/lib/auth/api-client', () => ({
  apiFetch: vi.fn(),
}));

const { apiFetch } = await import('@/lib/auth/api-client');
const apiFetchMock = vi.mocked(apiFetch);

function jsonResponse(
  body: unknown,
  init: ResponseInit & { etag?: string } = {},
): Response {
  const { etag, headers, ...rest } = init;
  const finalHeaders = new Headers(headers);
  if (!finalHeaders.has('Content-Type')) {
    finalHeaders.set('Content-Type', 'application/json');
  }
  if (etag) finalHeaders.set('ETag', `"${etag}"`);
  return new Response(JSON.stringify(body), {
    ...rest,
    status: rest.status ?? 200,
    headers: finalHeaders,
  });
}

function problemResponse(
  problem: Record<string, unknown>,
  status: number,
  extraHeaders: Record<string, string> = {},
): Response {
  return new Response(JSON.stringify(problem), {
    status,
    headers: {
      'Content-Type': 'application/problem+json',
      ...extraHeaders,
    },
  });
}

beforeEach(() => {
  apiFetchMock.mockReset();
});

afterEach(() => {
  vi.clearAllMocks();
});

describe('apiRequest — happy paths', () => {
  it('parsea JSON y captura el ETag', async () => {
    apiFetchMock.mockResolvedValueOnce(
      jsonResponse({ id: 'rq-1', folio: 'RQ-0001' }, { etag: '12' }),
    );

    const { data, etag, status } = await apiRequest<{
      id: string;
      folio: string;
    }>('/api/v1/compras/requisiciones/rq-1');

    expect(status).toBe(200);
    expect(data).toEqual({ id: 'rq-1', folio: 'RQ-0001' });
    expect(etag).toBe('12');
  });

  it('responde { data: undefined } a 204 No Content', async () => {
    apiFetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    const { data, status } = await apiRequest<void>('/api/v1/x', {
      method: 'POST',
    });

    expect(status).toBe(204);
    expect(data).toBeUndefined();
  });

  it('serializa body JSON y fija Content-Type cuando es objeto plano', async () => {
    apiFetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await apiRequest('/api/v1/x', {
      method: 'POST',
      body: { foo: 'bar' },
    });

    const [, init] = apiFetchMock.mock.calls[0];
    expect(init?.body).toBe(JSON.stringify({ foo: 'bar' }));
    const headers = new Headers(init?.headers);
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  it('NO toca Content-Type cuando el body ya es string', async () => {
    apiFetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await apiRequest('/api/v1/x', {
      method: 'POST',
      body: 'raw-payload',
      headers: { 'Content-Type': 'text/plain' },
    });

    const [, init] = apiFetchMock.mock.calls[0];
    expect(init?.body).toBe('raw-payload');
    expect(new Headers(init?.headers).get('Content-Type')).toBe('text/plain');
  });

  it('emite Idempotency-Key e If-Match cuando se pasan', async () => {
    apiFetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await apiRequest('/api/v1/x', {
      method: 'POST',
      body: {},
      idempotencyKey: 'idem-1',
      ifMatch: '12',
    });

    const [, init] = apiFetchMock.mock.calls[0];
    const headers = new Headers(init?.headers);
    expect(headers.get('Idempotency-Key')).toBe('idem-1');
    // El cliente envuelve el etag en comillas (RFC 7232).
    expect(headers.get('If-Match')).toBe('"12"');
  });
});

describe('apiRequest — errores', () => {
  it('parsea application/problem+json y lanza ApiError tipado', async () => {
    apiFetchMock.mockResolvedValueOnce(
      problemResponse(
        {
          type: 'about:blank',
          title: 'Validation failed',
          status: 422,
          code: 'TRANSMITIR_SIN_LINEAS',
          traceId: '00-abc-def-01',
          errores: [
            { campo: 'lineas', codigo: 'EMPTY', mensaje: 'Sin líneas' },
          ],
        },
        422,
      ),
    );

    await expect(
      apiRequest('/api/v1/compras/requisiciones/rq-1/transmitir', {
        method: 'POST',
        body: {},
      }),
    ).rejects.toMatchObject({
      name: 'ApiError',
      status: 422,
      code: 'TRANSMITIR_SIN_LINEAS',
      traceId: '00-abc-def-01',
      problem: { errores: [{ campo: 'lineas' }] },
    });
  });

  it('CONCURRENCY_CONFLICT (409) propaga sin reintentar', async () => {
    apiFetchMock.mockResolvedValueOnce(
      problemResponse(
        { type: 'about:blank', title: 'Conflict', status: 409, code: 'CONCURRENCY_CONFLICT' },
        409,
      ),
    );

    const sleep = vi.fn().mockResolvedValue(undefined);

    await expect(
      apiRequest('/api/v1/x', { method: 'POST', body: {}, sleep }),
    ).rejects.toSatisfy(esConflictoConcurrencia);
    expect(apiFetchMock).toHaveBeenCalledTimes(1);
    expect(sleep).not.toHaveBeenCalled();
  });

  it('responses sin Content-Type problem+json siguen lanzando ApiError', async () => {
    apiFetchMock.mockResolvedValueOnce(
      new Response('plain error', { status: 500, statusText: 'Server Error' }),
    );

    await expect(apiRequest('/api/v1/x')).rejects.toMatchObject({
      name: 'ApiError',
      status: 500,
    });
  });
});

describe('apiRequest — retry de IDEMPOTENCY_IN_PROGRESS', () => {
  it('reintenta automáticamente y devuelve éxito al cerrar el lock', async () => {
    apiFetchMock
      .mockResolvedValueOnce(
        problemResponse(
          {
            type: 'about:blank',
            title: 'In progress',
            status: 409,
            code: 'IDEMPOTENCY_IN_PROGRESS',
          },
          409,
          { 'Retry-After': '1' },
        ),
      )
      .mockResolvedValueOnce(
        problemResponse(
          {
            type: 'about:blank',
            title: 'In progress',
            status: 409,
            code: 'IDEMPOTENCY_IN_PROGRESS',
          },
          409,
          { 'Retry-After': '2' },
        ),
      )
      .mockResolvedValueOnce(jsonResponse({ id: 'rq-1' }));

    const sleep = vi.fn().mockResolvedValue(undefined);

    const { data } = await apiRequest<{ id: string }>('/api/v1/x', {
      method: 'POST',
      body: {},
      idempotencyKey: 'k',
      sleep,
    });

    expect(data).toEqual({ id: 'rq-1' });
    expect(apiFetchMock).toHaveBeenCalledTimes(3);
    // Respeta el Retry-After (en ms).
    expect(sleep).toHaveBeenNthCalledWith(1, 1000);
    expect(sleep).toHaveBeenNthCalledWith(2, 2000);
  });

  it('clampea Retry-After patológicamente largos al máximo (5 s)', async () => {
    apiFetchMock
      .mockResolvedValueOnce(
        problemResponse(
          {
            type: 'about:blank',
            title: 'In progress',
            status: 409,
            code: 'IDEMPOTENCY_IN_PROGRESS',
          },
          409,
          { 'Retry-After': '3600' }, // 1h
        ),
      )
      .mockResolvedValueOnce(jsonResponse({ ok: true }));

    const sleep = vi.fn().mockResolvedValue(undefined);

    await apiRequest('/api/v1/x', { method: 'POST', body: {}, sleep });

    expect(sleep).toHaveBeenCalledTimes(1);
    expect(sleep.mock.calls[0][0]).toBeLessThanOrEqual(5000);
  });

  it(`agota tras ${MAX_IDEMPOTENCY_RETRIES} reintentos y lanza ApiError`, async () => {
    const inProgress = () =>
      problemResponse(
        {
          type: 'about:blank',
          title: 'In progress',
          status: 409,
          code: 'IDEMPOTENCY_IN_PROGRESS',
        },
        409,
        { 'Retry-After': '0' },
      );

    for (let i = 0; i < MAX_IDEMPOTENCY_RETRIES + 1; i++) {
      apiFetchMock.mockResolvedValueOnce(inProgress());
    }

    const sleep = vi.fn().mockResolvedValue(undefined);

    await expect(
      apiRequest('/api/v1/x', { method: 'POST', body: {}, sleep }),
    ).rejects.toSatisfy(esIdempotencyEnCurso);

    expect(apiFetchMock).toHaveBeenCalledTimes(MAX_IDEMPOTENCY_RETRIES + 1);
  });

  it('ignora Retry-After con HTTP-date (formato no soportado) y usa default', async () => {
    apiFetchMock
      .mockResolvedValueOnce(
        problemResponse(
          {
            type: 'about:blank',
            title: 'In progress',
            status: 409,
            code: 'IDEMPOTENCY_IN_PROGRESS',
          },
          409,
          { 'Retry-After': 'Wed, 21 Oct 2026 07:28:00 GMT' },
        ),
      )
      .mockResolvedValueOnce(jsonResponse({ ok: true }));

    const sleep = vi.fn().mockResolvedValue(undefined);

    await apiRequest('/api/v1/x', { method: 'POST', body: {}, sleep });

    // Default sin parseo: el cliente cae a 100 ms (delay base interno).
    expect(sleep).toHaveBeenCalledWith(100);
  });
});
