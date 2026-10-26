import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineForm } from '@/features/compras/components/LineaInlineForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { LineaResponse } from '@/features/compras/api/types';

const ART = '22222222-2222-4222-8222-222222222222';
const CC = '0c000000-0000-4000-8000-000000000001';
const CLASE = '33333333-3333-4333-8333-333333333333';

const LINEA = {
  id: 'l-1',
  posicion: 1,
  articuloId: ART,
  cantidad: 2,
  unidadMedida: 'PZA',
  precioEstimadoMonto: 100,
  precioEstimadoMoneda: 'MXN',
  cuentaContableId: null,
  centroCostoId: CC,
  proyecto: null,
  fechaRequerida: null,
  notas: null,
  esActivoFijo: true,
  claseActivoId: CLASE,
} as unknown as LineaResponse;

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    http.get('*/api/v1/compras/requisiciones/:id/lineas/centro-costo-captura', () =>
      HttpResponse.json({ heredado: null, puedeElegir: true, unicaOpcion: null, mensaje: null }),
    ),
    http.get('*/api/v1/activos-fijos/clases', () =>
      HttpResponse.json([{ id: CLASE, nombre: 'Maquinaria', activa: true }]),
    ),
  );
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

describe('<LineaInlineForm> — activo fijo en edición', () => {
  it('prellena el flag y la clase y los envía en el PATCH', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/requisiciones/rq-1/lineas/l-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<LineaInlineForm requisicionId="rq-1" linea={LINEA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('checkbox', { name: /activo fijo/i })).toBeChecked();
    fireEvent.click(screen.getByRole('checkbox', { name: /activo fijo/i }));
    const guardar = screen.getByRole('button', { name: /guardar/i });
    await waitFor(() => expect(guardar).toBeEnabled());
    fireEvent.click(guardar);
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ esActivoFijo: false, claseActivoId: null });
  });
});
