import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArticulosLayout } from '@/modules/datos-maestros/components/ArticulosLayout';
import { NuevoArticuloContext } from '@/modules/datos-maestros/components/nuevo-articulo-context';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** F-02 (recorrido 04-nov): el buscador principal manda `q` (clave o descripción) a la API. */
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...rest }: { children: React.ReactNode; to: string; [key: string]: unknown }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
}));

const urls: URL[] = [];

beforeEach(() => {
  urls.length = 0;
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosArticulosGestionar],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/datos-maestros/articulos', ({ request }) => {
      urls.push(new URL(request.url));
      return HttpResponse.json({ items: [], total: 0, offset: 0, limit: 50 });
    }),
  );
});

afterEach(() => {
  mswServer.resetHandlers();
  useAuthStore.setState({ status: 'idle', accessToken: null, user: null, permisos: [] });
});

describe('ArticulosLayout: búsqueda principal', () => {
  it('busca por clave con el parámetro q', async () => {
    render(
      <NuevoArticuloContext.Provider value={{ abrir: () => {}, cerrar: () => {}, setDirty: () => {} }}>
        <ArticulosLayout idActivo={null} />
      </NuevoArticuloContext.Provider>,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.change(screen.getByRole('textbox', { name: 'Buscar por clave o descripción' }), {
      target: { value: 'AMC20329' },
    });

    await waitFor(() => expect(urls.some((u) => u.searchParams.get('q') === 'AMC20329')).toBe(true));
    expect(urls.every((u) => !u.searchParams.has('descripcion'))).toBe(true);
  });
});
