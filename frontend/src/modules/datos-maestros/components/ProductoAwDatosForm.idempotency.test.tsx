import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProductoAwDatosForm } from '@/modules/datos-maestros/components/ProductoAwDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ProductoAwDetalle } from '@/modules/datos-maestros/api/types';

/**
 * Regresión — Idempotency-Key por submit (ADR-0020, Bug B). Espejo del
 * test de artículo: el form de edición de producto A+W es multi-submit
 * (completar claves SAT en varias pasadas es el caso de uso normal);
 * la key se genera fresca por submit.
 */

const PRODUCTO: ProductoAwDetalle = {
  id: 'pa-1',
  referenciaExterna: 'VID-TEMP-6MM',
  descripcion: 'Vidrio templado 6 mm',
  unidadMedida: 'M2',
  unidadMedidaId: null,
  categoriaId: null,
  claveProdServSat: null,
  claveUnidadSat: null,
  objetoImp: null,
  tasaIvaTraslado: null,
  tasaRetencionIva: null,
  tasaRetencionIsr: null,
  origen: 1,
  datosFiscalesCompletos: false,
  estatus: 0,
};

beforeEach(() => {
  // <UnidadMedidaSelect> y <CategoriaSelector> disparan sus queries al
  // montar; listas vacías bastan.
  mswServer.use(
    http.get('*/api/v1/catalogos/unidades-medida', () =>
      HttpResponse.json([]),
    ),
    http.get('*/api/v1/catalogos/categorias-articulo', () =>
      HttpResponse.json([]),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosProductosAwGestionar],
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

describe('<ProductoAwDatosForm> — Idempotency-Key por submit (regresión Bug B)', () => {
  it('dos guardados del mismo producto envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.patch(
        '*/api/v1/datos-maestros/productos-aw/pa-1',
        async ({ request }) => {
          keys.push(request.headers.get('Idempotency-Key'));
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    render(<ProductoAwDatosForm producto={PRODUCTO} />, {
      wrapper: createQueryWrapper(),
    });

    const descripcion = screen.getByDisplayValue(
      'Vidrio templado 6 mm',
    ) as HTMLInputElement;
    const guardar = () =>
      screen.getByRole('button', { name: /guardar cambios/i });

    // Guardado #1.
    fireEvent.change(descripcion, {
      target: { value: 'Vidrio templado 6 mm claro' },
    });
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(1));

    // 2º cambio con body distinto (el form quedó montado).
    fireEvent.change(descripcion, {
      target: { value: 'Vidrio templado 6 mm gris' },
    });
    await waitFor(() => expect(guardar()).toBeEnabled());
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(2));

    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });

  it('completar la clave prod/serv SAT (vía ClaveSatSelector) viaja en el PATCH', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      // Catálogo SAT en vivo (FAC-DET-PR3): un match para la búsqueda.
      http.get('*/api/v1/catalogos/sat/clave-prod-serv', () =>
        HttpResponse.json([
          { codigo: '43211701', descripcion: 'Computadoras de escritorio' },
        ]),
      ),
      http.patch(
        '*/api/v1/datos-maestros/productos-aw/pa-1',
        async ({ request }) => {
          body = (await request.json()) as Record<string, unknown>;
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    render(<ProductoAwDatosForm producto={PRODUCTO} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(
      screen.getByRole('combobox', {
        name: /seleccionar clave sat \(clave-prod-serv\)/i,
      }),
    );
    fireEvent.change(
      screen.getByLabelText(/buscar clave sat por código o descripción/i),
      { target: { value: '43211701' } },
    );
    fireEvent.click(await screen.findByText('Computadoras de escritorio'));

    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({
      claveProdServSat: '43211701',
      // Sin categoría previa ni seleccionada → no limpiar.
      limpiarCategoria: false,
    });
  });
});
