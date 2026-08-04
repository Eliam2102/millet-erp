import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { usePermisos } from '@/modules/identidad/api/permisos';

describe('usePermisos', () => {
  it('agrupado=false: pide /permisos sin query param y devuelve solo items', async () => {
    let urlVisto = '';
    mswServer.use(
      http.get('*/api/v1/identidad/permisos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({
          items: [
            {
              id: 'p-1',
              codigo: 'identidad.roles.leer',
              modulo: 'identidad',
              recurso: 'roles',
              accion: 'leer',
              descripcion: 'Lee la lista de roles.',
            },
          ],
          grupos: null,
        });
      }),
    );

    const { result } = renderHook(() => usePermisos(false), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).not.toContain('agrupado=true');
    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.grupos).toBeNull();
  });

  it('agrupado=true: serializa el query param y devuelve grupos por módulo', async () => {
    let urlVisto = '';
    mswServer.use(
      http.get('*/api/v1/identidad/permisos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({
          items: [
            {
              id: 'p-1',
              codigo: 'identidad.roles.leer',
              modulo: 'identidad',
              recurso: 'roles',
              accion: 'leer',
              descripcion: 'Lee la lista de roles.',
            },
            {
              id: 'p-2',
              codigo: 'compras.requisiciones.leer',
              modulo: 'compras',
              recurso: 'requisiciones',
              accion: 'leer',
              descripcion: 'Lee requisiciones.',
            },
          ],
          grupos: [
            {
              modulo: 'identidad',
              items: [
                {
                  id: 'p-1',
                  codigo: 'identidad.roles.leer',
                  modulo: 'identidad',
                  recurso: 'roles',
                  accion: 'leer',
                  descripcion: 'Lee la lista de roles.',
                },
              ],
            },
            {
              modulo: 'compras',
              items: [
                {
                  id: 'p-2',
                  codigo: 'compras.requisiciones.leer',
                  modulo: 'compras',
                  recurso: 'requisiciones',
                  accion: 'leer',
                  descripcion: 'Lee requisiciones.',
                },
              ],
            },
          ],
        });
      }),
    );

    const { result } = renderHook(() => usePermisos(true), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('agrupado=true');
    expect(result.current.data?.grupos).toHaveLength(2);
    expect(result.current.data?.grupos?.[0]?.modulo).toBe('identidad');
    expect(result.current.data?.grupos?.[1]?.items).toHaveLength(1);
  });

  it('500: lanza error y queda en estado isError', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/permisos', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Error interno',
            status: 500,
            code: 'INTERNAL_ERROR',
          },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => usePermisos(true), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(500);
  });
});
