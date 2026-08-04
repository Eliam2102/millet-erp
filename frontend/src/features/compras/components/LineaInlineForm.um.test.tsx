import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineForm } from '@/features/compras/components/LineaInlineForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { Naturaleza, EstatusCatalogo } from '@/features/compras/api/types';

/**
 * Bug 2 — la UM de la línea de requisición se hereda del artículo
 * (UnidadMedidaDefault) al seleccionarlo, en un campo read-only. Verifica
 * además que read-only (no disabled) deja viajar el valor en el submit.
 */

// Id con forma de UUID: el schema valida articuloId contra el regex de
// UUID (los ids reales del catálogo son GUIDs).
const ART_ID = '11111111-1111-1111-1111-111111111111';

function mockCatalogo(unidadMedidaDefault: string) {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({
        items: [
          {
            id: ART_ID,
            clave: 'SOLV-001',
            nombre: 'Solvente industrial',
            unidadMedidaDefault,
            naturaleza: Naturaleza.Estandar,
            categoria: null,
            estatus: EstatusCatalogo.Activo,
          },
        ],
        offset: 0,
        limit: 50,
        total: 1,
      }),
    ),
    // Fase E PR2.1: prellenado del CC. Exactamente 1 Dim3 en alcance → el form
    // autocompleta centroCostoId, así el submit pasa la validación obligatoria.
    http.get('*/api/v1/centros-costo/dim3/buscar', () =>
      HttpResponse.json([
        {
          id: '0c000000-0000-0000-0000-000000000001',
          clave: 'MCLC101',
          nombre: 'Gantry',
          grupoDim3Nombre: 'Corte',
          dim2Clave: 'D2',
          dim2Nombre: 'Producción',
          dim1Clave: 'D1',
          dim1Nombre: 'Planta',
          estatus: 0,
        },
      ]),
    ),
  );
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'u@m.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [],
    errorMessage: null,
  });
});

async function seleccionarArticulo() {
  fireEvent.click(screen.getByRole('combobox', { name: /seleccionar artículo/i }));
  const item = await screen.findByText('Solvente industrial');
  fireEvent.click(item);
}

describe('<LineaInlineForm> — herencia de unidad de medida (Bug 2)', () => {
  it('al seleccionar un artículo, la UM se puebla con su UnidadMedidaDefault', async () => {
    mockCatalogo('LT');
    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    const um = screen.getByLabelText(
      /unidad de medida \(heredada del artículo\)/i,
    ) as HTMLInputElement;
    expect(um.value).toBe(''); // placeholder hasta elegir artículo

    await seleccionarArticulo();

    await waitFor(() => expect(um.value).toBe('LT'));
  });

  it('el campo UM es read-only (no disabled) para que viaje en el submit', async () => {
    mockCatalogo('LT');
    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    const um = screen.getByLabelText(
      /unidad de medida \(heredada del artículo\)/i,
    ) as HTMLInputElement;
    expect(um).toHaveAttribute('readonly');
    expect(um).not.toBeDisabled();
  });

  it('UM vacía en el catálogo cae al fallback PZA', async () => {
    mockCatalogo('');
    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await seleccionarArticulo();
    const um = screen.getByLabelText(
      /unidad de medida \(heredada del artículo\)/i,
    ) as HTMLInputElement;
    await waitFor(() => expect(um.value).toBe('PZA'));
  });

  it('la UM heredada viaja en el body del POST al guardar la línea', async () => {
    mockCatalogo('LT');
    let bodyEnviado: Record<string, unknown> | null = null;
    mswServer.use(
      http.post(
        '*/api/v1/compras/requisiciones/rq-1/lineas',
        async ({ request }) => {
          bodyEnviado = (await request.json()) as Record<string, unknown>;
          return HttpResponse.json({ id: 'l-new', posicion: 1 }, { status: 201 });
        },
      ),
    );

    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    await seleccionarArticulo();

    // cantidad ya viene en 1 (default); fijamos un precio > 0 (Zod lo exige).
    // El input del monto es el number contiguo al select "Moneda" del MoneyField.
    const moneda = screen.getByRole('combobox', { name: /moneda/i });
    const precioInput = moneda.parentElement?.querySelector(
      'input[type="number"]',
    ) as HTMLInputElement;
    fireEvent.change(precioInput, { target: { value: '100' } });

    fireEvent.click(screen.getByRole('button', { name: /agregar línea/i }));

    await waitFor(() => expect(bodyEnviado).not.toBeNull());
    expect(bodyEnviado).toMatchObject({
      articuloId: ART_ID,
      unidadMedida: 'LT',
      cantidad: 1,
      precioEstimadoMonto: 100,
    });
  });
});
