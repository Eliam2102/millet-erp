import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArticuloDatosForm } from '@/modules/datos-maestros/components/ArticuloDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ArticuloDetalle } from '@/modules/datos-maestros/api/types';

/** F1-MP-01 S1 (E07 D5): vínculo artículo ↔ producto A+W desde el form de datos. */

const PRODUCTO = {
  id: 'p-1',
  referenciaExterna: '2',
  descripcion: 'VIDRIO FLOAT CLARO 6MM',
  unidadMedida: 'm²',
  claveProdServSat: null,
  datosFiscalesCompletos: true,
};

const ARTICULO: ArticuloDetalle = {
  id: 'a-1',
  clave: 'VID-CLARO-6',
  claveLegacy: null,
  nombre: 'Vidrio float claro 6 mm',
  descripcionLarga: null,
  unidadMedidaDefault: 'M2',
  unidadMedidaId: null,
  naturaleza: 0,
  categoria: null,
  categoriaId: null,
  precioReferenciaMonto: null,
  precioReferenciaMoneda: null,
  estatus: 0,
  productoAwId: null,
};

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/unidades-medida', () => HttpResponse.json([])),
    http.get('*/api/v1/facturacion/catalogos/productos-aw', () => HttpResponse.json([PRODUCTO])),
    http.get('*/api/v1/datos-maestros/productos-aw/p-1', () =>
      HttpResponse.json({ ...PRODUCTO, variantes: [], version: 1 }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [
      PermisosCanonicos.CompartidoCatalogosAdministrar,
      PermisosCanonicos.FacturacionFacturasEmitir,
      PermisosCanonicos.DatosMaestrosProductosAwGestionar,
    ],
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

async function elegirProducto() {
  fireEvent.click(screen.getByRole('combobox', { name: /producto a\+w/i }));
  fireEvent.click(await screen.findByText('VIDRIO FLOAT CLARO 6MM'));
}

describe('<ArticuloDatosForm> — producto A+W', () => {
  it('liga: envía productoAwId sin limpiar', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/catalogos/articulos/a-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<ArticuloDatosForm articulo={ARTICULO} />, { wrapper: createQueryWrapper() });

    await elegirProducto();
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ productoAwId: 'p-1', limpiarProductoAw: false });
  });

  it('desliga: etiqueta del producto ligado y limpiarProductoAw al deseleccionar', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/catalogos/articulos/a-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<ArticuloDatosForm articulo={{ ...ARTICULO, productoAwId: 'p-1' }} />, {
      wrapper: createQueryWrapper(),
    });

    expect(await screen.findByText('2 · VIDRIO FLOAT CLARO 6MM')).toBeInTheDocument();
    await elegirProducto(); // mismo item = deselección
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ productoAwId: null, limpiarProductoAw: true });
  });

  it.each([
    [PermisosCanonicos.FacturacionFacturasEmitir],
    [PermisosCanonicos.DatosMaestrosProductosAwGestionar],
  ])('oculta el campo si falta el permiso %s', (faltante) => {
    useAuthStore.setState({
      permisos: [
        PermisosCanonicos.CompartidoCatalogosAdministrar,
        PermisosCanonicos.FacturacionFacturasEmitir,
        PermisosCanonicos.DatosMaestrosProductosAwGestionar,
      ].filter((p) => p !== faltante),
    });
    render(<ArticuloDatosForm articulo={ARTICULO} />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('combobox', { name: /producto a\+w/i })).not.toBeInTheDocument();
  });
});
