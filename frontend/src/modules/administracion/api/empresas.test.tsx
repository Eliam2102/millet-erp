import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createQueryWrapper,
  createTestQueryClient,
} from '@/test/test-query-client';
import {
  useActualizarEmpresa,
  useCrearEmpresa,
  useDesactivarEmpresa,
  useEmpresa,
  useEmpresas,
} from '@/modules/administracion/api/empresas';
import { adminKeys } from '@/modules/administracion/api/keys';

function makeEmpresa(overrides: Record<string, unknown> = {}) {
  return {
    id: 'e-1',
    rfc: 'MIL010101ABC',
    razonSocial: 'Millet S.A. de C.V.',
    nombreComercial: 'Millet',
    regimenFiscal: '601',
    activa: true,
    version: 1,
    ...overrides,
  };
}

describe('useEmpresas', () => {
  it('200 OK: devuelve { items, total }', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [makeEmpresa()], total: 1 }),
      ),
    );

    const { result } = renderHook(() => useEmpresas(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.total).toBe(1);
    expect(result.current.data?.items[0]?.rfc).toBe('MIL010101ABC');
  });

  it('serializa filtros en query string', async () => {
    let urlVisto: string = '';
    mswServer.use(
      http.get('*/api/v1/admin/empresas', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], total: 0 });
      }),
    );

    const { result } = renderHook(
      () => useEmpresas({ soloActivas: true, offset: 10, limit: 25 }),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('soloActivas=true');
    expect(urlVisto).toContain('offset=10');
    expect(urlVisto).toContain('limit=25');
  });
});

describe('useEmpresa', () => {
  it('200 OK: devuelve empresa + sucursales + departamentos', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json({
          empresa: makeEmpresa(),
          sucursales: [
            { id: 's-1', clave: 'MID', nombre: 'Mérida', estatus: 0, version: 1 },
          ],
          departamentos: [
            { id: 'd-1', clave: 'COMP', nombre: 'Compras', estatus: 0, version: 1 },
          ],
        }),
      ),
    );

    const { result } = renderHook(() => useEmpresa('e-1'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.empresa.rfc).toBe('MIL010101ABC');
    expect(result.current.data?.sucursales).toHaveLength(1);
    expect(result.current.data?.departamentos).toHaveLength(1);
  });

  it('id null inhabilita el query', async () => {
    let llamado = false;
    mswServer.use(
      http.get('*/api/v1/admin/empresas/anything', () => {
        llamado = true;
        return HttpResponse.json({});
      }),
    );

    const { result } = renderHook(() => useEmpresa(null), {
      wrapper: createQueryWrapper(),
    });

    await new Promise((r) => setTimeout(r, 10));
    expect(result.current.fetchStatus).toBe('idle');
    expect(llamado).toBe(false);
  });

  it('404: lanza ApiError 404', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/no-existe', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'No encontrada',
            status: 404,
            code: 'EMPRESA_NO_ENCONTRADA',
          },
          {
            status: 404,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useEmpresa('no-existe'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(404);
  });
});

describe('useCrearEmpresa', () => {
  it('201: invalida adminKeys.empresas() en éxito', async () => {
    let bodyVisto: unknown = null;
    let idemVisto: string | null = null;

    mswServer.use(
      http.post('*/api/v1/admin/empresas', async ({ request }) => {
        bodyVisto = await request.json();
        idemVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(makeEmpresa({ id: 'e-new' }), { status: 201 });
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCrearEmpresa(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      command: {
        rfc: 'MIL010101ABC',
        razonSocial: 'Millet S.A. de C.V.',
        regimenFiscal: '601',
        nombreComercial: 'Millet',
      },
      idempotencyKey: 'idem-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idemVisto).toBe('idem-1');
    expect(bodyVisto).toMatchObject({ rfc: 'MIL010101ABC' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresas(),
    });
  });

  it('409 EMPRESA_RFC_DUPLICADO propaga al caller', async () => {
    mswServer.use(
      http.post('*/api/v1/admin/empresas', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'EMPRESA_RFC_DUPLICADO',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearEmpresa(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      command: {
        rfc: 'DUP010101ABC',
        razonSocial: 'X',
        regimenFiscal: '601',
        nombreComercial: null,
      },
      idempotencyKey: 'idem-2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(409);
    expect(err.code).toBe('EMPRESA_RFC_DUPLICADO');
  });
});

describe('useActualizarEmpresa', () => {
  it('200: PATCH envía solo los campos del payload e invalida detalle + lista', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.patch('*/api/v1/admin/empresas/e-1', async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json(makeEmpresa({ razonSocial: 'Nuevo nombre' }));
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useActualizarEmpresa(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      id: 'e-1',
      payload: { razonSocial: 'Nuevo nombre' },
      idempotencyKey: 'idem-patch',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toEqual({ razonSocial: 'Nuevo nombre' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresa('e-1'),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresas(),
    });
  });
});

describe('useDesactivarEmpresa', () => {
  it('200: POST /desactivar invalida detalle + lista', async () => {
    mswServer.use(
      http.post('*/api/v1/admin/empresas/e-1/desactivar', () =>
        HttpResponse.json(makeEmpresa({ activa: false })),
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDesactivarEmpresa(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({ id: 'e-1', idempotencyKey: 'idem-des' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.activa).toBe(false);
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresa('e-1'),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresas(),
    });
  });
});
