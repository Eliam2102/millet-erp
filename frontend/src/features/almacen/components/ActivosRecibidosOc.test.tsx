import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ActivosRecibidosOc } from '@/features/almacen/components/ActivosRecibidosOc';
import { useAuthStore } from '@/lib/auth/auth-store';

const OC = 'oc-1';
const fila = (o: object) => ({
  id: 'a1',
  recepcionId: 'r-1',
  folio: null,
  lineaOcId: 'l-1',
  cantidad: 2,
  recibidaEn: '2026-06-01T10:00:00Z',
  anulada: false,
  anuladaEn: null,
  motivoAnulacion: null,
  ...o,
});

beforeEach(() => {
  mswServer.use(
    http.get(`*/api/v1/almacen/recepciones/ordenes/${OC}/activos`, () =>
      HttpResponse.json([
        fila({}),
        fila({ id: 'a2', lineaOcId: 'l-2' }),
        fila({
          id: 'a3',
          recepcionId: 'r-2',
          folio: 'ACT-2',
          anulada: true,
          motivoAnulacion: 'Error',
        }),
      ]),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'u@m.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: ['almacen.entradas.leer', 'almacen.entradas.registrar'],
    errorMessage: null,
  });
});

describe('<ActivosRecibidosOc>', () => {
  it('exige motivo y llama al endpoint, una acción por recepción', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post(
        `*/api/v1/almacen/recepciones/ordenes/${OC}/activos/r-1/anular`,
        async ({ request }) => {
          body = (await request.json()) as Record<string, unknown>;
          return HttpResponse.json({
            recepcionId: 'r-1',
            folio: null,
            lineasAnuladas: 2,
            yaAnulada: false,
          });
        },
      ),
    );
    render(<ActivosRecibidosOc ordenCompraId={OC} />, { wrapper: createQueryWrapper() });
    expect(await screen.findAllByText('Sin folio')).toHaveLength(2);
    expect(screen.getByText(/Error/)).toBeInTheDocument();
    const botones = screen.getAllByRole('button', { name: 'Anular recepción' });
    expect(botones).toHaveLength(1);
    fireEvent.click(botones[0]);
    fireEvent.click(
      await screen
        .findByRole('button', { name: 'Anular recepción', hidden: false, description: '' })
        .catch(() => screen.getAllByRole('button', { name: 'Anular recepción' })[1]),
    );
    expect(await screen.findByText('Motivo requerido')).toBeInTheDocument();
    expect(body).toBeNull();
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Captura errónea' } });
    const confirmar = screen.getAllByRole('button', { name: 'Anular recepción' });
    fireEvent.click(confirmar[confirmar.length - 1]);
    await waitFor(() => expect(body).toEqual({ motivo: 'Captura errónea' }));
  });
});
