import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { Activos } from '@/features/facturacion/pages/Activos';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 't',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' },
    empresas: [],
    currentEmpresaId: 'e',
    permisos: ['facturacion.activos.autorizar'],
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

describe('<Activos> — smoke', () => {
  it('lista autorizaciones y muestra "Autorizar venta" con permiso', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/activos/autorizaciones', () =>
        HttpResponse.json([
          {
            id: 'au-1',
            activoRef: 'AF-1',
            descripcion: 'Montacargas',
            precioVenta: 50000,
            valorNetoEnLibros: 30000,
            utilidadOPerdida: 20000,
            autorizadoPor: 'u-1',
            fechaAutorizacion: '2026-05-30T10:00:00Z',
            estado: 'Autorizada',
            motivo: 5,
            solicitadoPor: 'Transportes',
            evidencia: 'Factura DEMO',
          },
        ]),
      ),
    );
    render(<Activos />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Montacargas')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Autorizar venta/i })).toBeInTheDocument();
  });

  it('exige motivo, área y soporte y envía la autorización del Director de Administración y Finanzas', async () => {
    let recibido: unknown;
    mswServer.use(
      http.get('*/api/v1/facturacion/activos/autorizaciones', () => HttpResponse.json([])),
      http.post('*/api/v1/facturacion/activos/autorizar', async ({ request }) => {
        recibido = await request.json();
        return HttpResponse.json(
          {
            autorizacionId: 'a',
            descripcion: 'Activo',
            valorNetoEnLibros: 10,
            utilidadOPerdida: 10,
          },
          { status: 201 },
        );
      }),
    );
    render(<Activos />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar venta' }));
    expect(screen.getAllByText(/Director de Administración y Finanzas/).length).toBeGreaterThan(0);
    const autorizar = screen.getByRole('button', { name: 'Autorizar' });
    expect(autorizar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Referencia del activo *'), {
      target: { value: 'AF-1' },
    });
    fireEvent.change(screen.getByLabelText('Precio de venta *'), { target: { value: '20' } });
    fireEvent.change(screen.getByLabelText('Motivo *'), { target: { value: '5' } });
    fireEvent.change(screen.getByLabelText('Solicitado por (área usuaria) *'), {
      target: { value: 'Transportes' },
    });
    expect(autorizar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Referencia del soporte *'), {
      target: { value: 'Factura DEMO' },
    });
    expect(autorizar).toBeEnabled();
    fireEvent.click(autorizar);
    await waitFor(() =>
      expect(recibido).toEqual({
        activoRef: 'AF-1',
        precioVenta: 20,
        motivo: 5,
        solicitadoPor: 'Transportes',
        evidencia: 'Factura DEMO',
      }),
    );
  });

  it('empty', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/activos/autorizaciones', () => HttpResponse.json([])),
    );
    render(<Activos />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText(/Sin autorizaciones/i)).toBeInTheDocument());
  });
});
