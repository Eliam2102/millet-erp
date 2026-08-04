import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useDepartamentos,
  useUsuarios,
  useSucursales,
  useAlmacenes,
  mapById,
  esCatalogoSinPermiso,
} from '@/features/catalogos/api/hooks';
import { ApiError } from '@/lib/api';

describe('useDepartamentos', () => {
  it('devuelve la lista paginada del endpoint', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/departamentos', () =>
        HttpResponse.json({
          items: [
            { id: 'd-1', clave: 'COMPRAS', nombre: 'Compras', estatus: 0 },
            { id: 'd-2', clave: 'ALMACEN', nombre: 'Almacén', estatus: 0 },
          ],
          offset: 0,
          limit: 200,
          total: 2,
        }),
      ),
    );

    const { result } = renderHook(() => useDepartamentos(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items).toHaveLength(2);
  });

  it('403 sin permiso: el query expone el error', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/departamentos', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Forbidden', status: 403 },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useDepartamentos(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(esCatalogoSinPermiso(result.current.error)).toBe(true);
  });
});

describe('useSucursales / useAlmacenes / useUsuarios — smoke', () => {
  it('useSucursales: llama al endpoint correcto', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/sucursales', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 });
      }),
    );
    const { result } = renderHook(() => useSucursales(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('/api/v1/catalogos/sucursales');
  });

  it('useAlmacenes(sucursalId): inyecta sucursalId como query param', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/almacenes', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 });
      }),
    );
    const { result } = renderHook(() => useAlmacenes('s-99'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('sucursalId=s-99');
  });

  it('useUsuarios: llama al endpoint con identidad.usuarios.leer', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/identidad/usuarios', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 });
      }),
    );
    const { result } = renderHook(() => useUsuarios(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('/api/v1/identidad/usuarios');
  });
});

describe('helpers', () => {
  it('mapById: convierte lista a Map por id', () => {
    const items = [
      { id: 'a', nombre: 'A' },
      { id: 'b', nombre: 'B' },
    ];
    const map = mapById(items);
    expect(map.size).toBe(2);
    expect(map.get('a')?.nombre).toBe('A');
  });

  it('mapById con undefined devuelve Map vacío', () => {
    expect(mapById(undefined).size).toBe(0);
  });

  it('esCatalogoSinPermiso: discrimina 403 vs otros', () => {
    const err403 = new ApiError(
      { type: 'about:blank', title: 'F', status: 403 },
      403,
    );
    const err500 = new ApiError(
      { type: 'about:blank', title: 'E', status: 500 },
      500,
    );
    expect(esCatalogoSinPermiso(err403)).toBe(true);
    expect(esCatalogoSinPermiso(err500)).toBe(false);
    expect(esCatalogoSinPermiso(new Error('boom'))).toBe(false);
  });
});
