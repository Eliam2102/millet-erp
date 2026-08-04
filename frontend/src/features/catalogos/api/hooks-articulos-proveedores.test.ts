import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useArticulos,
  useProveedores,
} from '@/features/catalogos/api';
import {
  EstatusCatalogo,
  Naturaleza,
} from '@/features/compras/api/types';

describe('useArticulos', () => {
  it('inyecta estatus=Activo por default y limit', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({
          items: [],
          offset: 0,
          limit: 50,
          total: 0,
        });
      }),
    );

    const { result } = renderHook(() => useArticulos(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain('estatus=' + EstatusCatalogo.Activo);
    expect(urlVisto).toContain('limit=50');
  });

  it('respeta override de estatus si el caller lo pasa', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(
      () => useArticulos({ estatus: EstatusCatalogo.Inactivo }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain(`estatus=${EstatusCatalogo.Inactivo}`);
  });

  it('inyecta clave y naturaleza cuando se pasan', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(
      () => useArticulos({ clave: 'TORN', naturaleza: Naturaleza.Critico }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain('clave=TORN');
    expect(urlVisto).toContain(`naturaleza=${Naturaleza.Critico}`);
  });

  it('omite clave cuando es undefined o vacía', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(() => useArticulos({}), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).not.toContain('clave=');
  });

  it('inyecta nombre cuando se pasa (sin clave)', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(
      () => useArticulos({ nombre: 'papeleria' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain('nombre=papeleria');
    expect(urlVisto).not.toContain('clave=');
  });

  it('clave tiene precedencia: si llegan clave y nombre, solo viaja clave', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(
      () => useArticulos({ clave: 'TORN', nombre: 'papeleria' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain('clave=TORN');
    expect(urlVisto).not.toContain('nombre=');
  });

  it('clave vacía/whitespace no contamina: se omite y aplica nombre', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    // Simula "limpiar" la caja de clave (queda whitespace) buscando por
    // nombre: la clave vacía NO debe ganar precedencia ni viajar en la URL.
    const { result } = renderHook(
      () => useArticulos({ clave: '   ', nombre: 'papeleria' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain('nombre=papeleria');
    expect(urlVisto).not.toContain('clave=');
  });
});

describe('useProveedores', () => {
  it('inyecta estatus=Activo + limit por default', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(() => useProveedores(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain('estatus=' + EstatusCatalogo.Activo);
    expect(urlVisto).toContain('limit=50');
  });

  it('inyecta clave de búsqueda', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(
      () => useProveedores({ clave: 'PROV-001' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(urlVisto).toContain('clave=PROV-001');
  });
});
