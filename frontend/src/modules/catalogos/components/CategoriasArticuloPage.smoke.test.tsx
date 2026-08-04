import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CategoriasArticuloPage } from '@/modules/catalogos/components/CategoriasArticuloPage';
import { NuevaCategoriaArticuloProvider } from '@/modules/catalogos/components/SheetNuevaCategoriaArticulo';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke de la pantalla admin de Categorías de artículo (patrón ADR-0046).
 * Reuso de CatalogoEditableTable; lista vía GET /catalogos/categorias-articulo.
 */

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.CompartidoCatalogosAdministrar],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<CategoriasArticuloPage> — smoke', () => {
  it('lista las categorías del catálogo y muestra el botón Nueva', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/categorias-articulo', () =>
        HttpResponse.json([
          { id: 'c-1', nombre: 'MAT DE LIMPIEZA', estatus: 0, version: 1 },
          { id: 'c-2', nombre: 'Químicos', estatus: 0, version: 1 },
        ]),
      ),
    );

    render(
      <NuevaCategoriaArticuloProvider>
        <CategoriasArticuloPage />
      </NuevaCategoriaArticuloProvider>,
      { wrapper: createQueryWrapper() },
    );

    expect(
      screen.getByRole('heading', { name: /categorías de artículo/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /nueva categoría/i }),
    ).toBeInTheDocument();

    expect(await screen.findByText('MAT DE LIMPIEZA')).toBeInTheDocument();
    expect(screen.getByText('Químicos')).toBeInTheDocument();
  });
});
