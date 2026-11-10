import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArticuloDatosForm } from '@/modules/datos-maestros/components/ArticuloDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ArticuloDetalle } from '@/modules/datos-maestros/api/types';

/** M-172: marca «Se consume por tambor a línea» y kilos por presentación en la edición del artículo. */

const SILICON: ArticuloDetalle = {
  id: 'a-1',
  clave: 'SIL-A',
  claveLegacy: null,
  nombre: 'Silicón componente A',
  descripcionLarga: null,
  unidadMedidaDefault: 'PZA',
  unidadMedidaId: null,
  naturaleza: 0,
  categoria: null,
  categoriaId: null,
  precioReferenciaMonto: null,
  precioReferenciaMoneda: null,
  estatus: 0,
  consumoPorTamborALinea: false,
  kgPorPresentacion: null,
};

let body: Record<string, unknown> | null;

beforeEach(() => {
  body = null;
  mswServer.use(
    http.get('*/api/v1/catalogos/unidades-medida', () => HttpResponse.json([])),
    http.patch('*/api/v1/catalogos/articulos/a-1', async ({ request }) => {
      body = (await request.json()) as Record<string, unknown>;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  useAuthStore.setState({ permisos: [PermisosCanonicos.CompartidoCatalogosAdministrar] });
});

afterEach(() => useAuthStore.setState({ permisos: [] }));

describe('<ArticuloDatosForm> — consumo por tambor a línea', () => {
  it('marca el artículo y guarda los kilos por presentación', async () => {
    render(<ArticuloDatosForm articulo={SILICON} />, { wrapper: createQueryWrapper() });

    fireEvent.click(screen.getByRole('checkbox', { name: /se consume por tambor a línea/i }));
    fireEvent.change(screen.getByLabelText(/kilos por presentación/i), { target: { value: '200' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ consumoPorTamborALinea: true, kgPorPresentacion: 200, limpiarKgPorPresentacion: false });
  });

  it('quitar los kilos manda limpiarKgPorPresentacion', async () => {
    render(<ArticuloDatosForm articulo={{ ...SILICON, consumoPorTamborALinea: true, kgPorPresentacion: 19 }} />, {
      wrapper: createQueryWrapper(),
    });

    expect(screen.getByRole('checkbox', { name: /se consume por tambor a línea/i })).toBeChecked();
    fireEvent.change(screen.getByLabelText(/kilos por presentación/i), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ consumoPorTamborALinea: true, kgPorPresentacion: null, limpiarKgPorPresentacion: true });
  });
});
