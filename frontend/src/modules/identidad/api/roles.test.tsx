import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createQueryWrapper,
  createTestQueryClient,
} from '@/test/test-query-client';
import {
  useActualizarRol,
  useAsignarPermisos,
  useAsociarGrupoEntraId,
  useCrearRol,
  useDesasociarGrupoEntraId,
  useEliminarRol,
  useRol,
  useRoles,
} from '@/modules/identidad/api/roles';
import { identidadKeys } from '@/modules/identidad/api/keys';

function makeRol(overrides: Record<string, unknown> = {}) {
  return {
    id: 'r-1',
    codigo: 'admin-compras',
    nombre: 'Administrador de Compras',
    descripcion: 'Gestiona requisiciones y órdenes de compra.',
    esDelSistema: false,
    activo: true,
    version: 1,
    ...overrides,
  };
}

describe('useRoles', () => {
  it('200 OK: devuelve { items, total }', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/roles', () =>
        HttpResponse.json({ items: [makeRol()], total: 1 }),
      ),
    );

    const { result } = renderHook(() => useRoles(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.total).toBe(1);
    expect(result.current.data?.items[0]?.codigo).toBe('admin-compras');
  });

  it('serializa filtros en query string', async () => {
    let urlVisto = '';
    mswServer.use(
      http.get('*/api/v1/identidad/roles', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], total: 0 });
      }),
    );

    const { result } = renderHook(
      () => useRoles({ soloActivos: true, offset: 10, limit: 25 }),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('soloActivos=true');
    expect(urlVisto).toContain('offset=10');
    expect(urlVisto).toContain('limit=25');
  });
});

describe('useRol', () => {
  it('200 OK: devuelve rol + permisoIds + gruposEntraId', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/roles/r-1', () =>
        HttpResponse.json({
          rol: makeRol(),
          permisoIds: ['p-1', 'p-2'],
          gruposEntraId: [
            {
              id: 'g-1',
              rolId: 'r-1',
              objectId: 'oid-1',
              nombre: 'Compras - Aprobadores',
            },
          ],
        }),
      ),
    );

    const { result } = renderHook(() => useRol('r-1'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.rol.codigo).toBe('admin-compras');
    expect(result.current.data?.permisoIds).toHaveLength(2);
    expect(result.current.data?.gruposEntraId).toHaveLength(1);
  });

  it('id null inhabilita el query', async () => {
    let llamado = false;
    mswServer.use(
      http.get('*/api/v1/identidad/roles/anything', () => {
        llamado = true;
        return HttpResponse.json({});
      }),
    );

    const { result } = renderHook(() => useRol(null), {
      wrapper: createQueryWrapper(),
    });

    await new Promise((r) => setTimeout(r, 10));
    expect(result.current.fetchStatus).toBe('idle');
    expect(llamado).toBe(false);
  });

  it('404: lanza ApiError 404', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/roles/no-existe', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'No encontrado',
            status: 404,
            code: 'ROL_NO_ENCONTRADO',
          },
          {
            status: 404,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useRol('no-existe'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(404);
  });
});

describe('useCrearRol', () => {
  it('201: invalida identidadKeys.roles() en éxito', async () => {
    let bodyVisto: unknown = null;
    let idemVisto: string | null = null;

    mswServer.use(
      http.post('*/api/v1/identidad/roles', async ({ request }) => {
        bodyVisto = await request.json();
        idemVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(makeRol({ id: 'r-new' }), { status: 201 });
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCrearRol(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      command: {
        codigo: 'admin-compras',
        nombre: 'Administrador de Compras',
        descripcion: null,
      },
      idempotencyKey: 'idem-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idemVisto).toBe('idem-1');
    expect(bodyVisto).toMatchObject({ codigo: 'admin-compras' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: identidadKeys.roles(),
    });
  });

  it('409 ROL_CODIGO_DUPLICADO propaga al caller', async () => {
    mswServer.use(
      http.post('*/api/v1/identidad/roles', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'ROL_CODIGO_DUPLICADO',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearRol(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      command: { codigo: 'dup', nombre: 'X', descripcion: null },
      idempotencyKey: 'idem-2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(409);
    expect(err.code).toBe('ROL_CODIGO_DUPLICADO');
  });
});

describe('useActualizarRol', () => {
  it('200: PATCH envía solo los campos del payload e invalida detalle + lista', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.patch('*/api/v1/identidad/roles/r-1', async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json(makeRol({ nombre: 'Nuevo nombre' }));
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useActualizarRol(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      id: 'r-1',
      payload: { nombre: 'Nuevo nombre' },
      idempotencyKey: 'idem-patch',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toEqual({ nombre: 'Nuevo nombre' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: identidadKeys.rol('r-1'),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: identidadKeys.roles(),
    });
  });
});

describe('useEliminarRol', () => {
  it('204: DELETE invalida detalle + lista', async () => {
    mswServer.use(
      http.delete('*/api/v1/identidad/roles/r-1', () =>
        HttpResponse.text('', { status: 204 }),
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useEliminarRol(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({ id: 'r-1', idempotencyKey: 'idem-del' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: identidadKeys.rol('r-1'),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: identidadKeys.roles(),
    });
  });

  it('409 ROL_DEL_SISTEMA propaga al caller', async () => {
    mswServer.use(
      http.delete('*/api/v1/identidad/roles/r-sys', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'ROL_DEL_SISTEMA',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useEliminarRol(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({ id: 'r-sys', idempotencyKey: 'idem-x' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(409);
    expect(err.code).toBe('ROL_DEL_SISTEMA');
  });
});

describe('useAsignarPermisos', () => {
  it('200: PUT envía permisoIds como batch atómico e invalida detalle', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.put(
        '*/api/v1/identidad/roles/r-1/permisos',
        async ({ request }) => {
          bodyVisto = await request.json();
          return HttpResponse.text('', { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useAsignarPermisos(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      id: 'r-1',
      command: { permisoIds: ['p-1', 'p-2', 'p-3'] },
      idempotencyKey: 'idem-perm',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toEqual({ permisoIds: ['p-1', 'p-2', 'p-3'] });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: identidadKeys.rol('r-1'),
    });
  });
});

describe('useAsociarGrupoEntraId', () => {
  it('201: POST agrega grupo y devuelve el response', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.post(
        '*/api/v1/identidad/roles/r-1/grupos-entra-id',
        async ({ request }) => {
          bodyVisto = await request.json();
          return HttpResponse.json(
            {
              id: 'g-new',
              rolId: 'r-1',
              objectId: 'oid-xyz',
              nombre: 'Compras',
            },
            { status: 201 },
          );
        },
      ),
    );

    const { result } = renderHook(() => useAsociarGrupoEntraId(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      rolId: 'r-1',
      command: { objectId: 'oid-xyz', nombre: 'Compras' },
      idempotencyKey: 'idem-g',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toEqual({ objectId: 'oid-xyz', nombre: 'Compras' });
    expect(result.current.data?.id).toBe('g-new');
  });
});

describe('useDesasociarGrupoEntraId', () => {
  it('204: DELETE invalida el detalle del rol', async () => {
    mswServer.use(
      http.delete(
        '*/api/v1/identidad/roles/grupos-entra-id/g-1',
        () => HttpResponse.text('', { status: 204 }),
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDesasociarGrupoEntraId(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      rolId: 'r-1',
      grupoId: 'g-1',
      idempotencyKey: 'idem-d',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: identidadKeys.rol('r-1'),
    });
  });
});
