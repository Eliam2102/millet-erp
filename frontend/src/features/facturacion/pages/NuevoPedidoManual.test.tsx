import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevoPedidoManual } from '@/features/facturacion/pages/NuevoPedidoManual';
import { useAuthStore } from '@/lib/auth/auth-store';

// Los selectores consultan catálogos; aquí se reemplazan por botones que
// fijan un valor para poder enviar el form sin red.
vi.mock('@/components/erp', async (importOriginal) => {
  const real = await importOriginal<typeof import('@/components/erp')>();
  return {
    ...real,
    SucursalSelector: ({ onChange }: { onChange: (id: string | null) => void }) => (
      <button
        type="button"
        onClick={() => onChange('33333333-3333-4333-8333-333333333333')}
      >
        Elegir sucursal
      </button>
    ),
  };
});
vi.mock('@/features/facturacion/components/selectors/ClienteSelector', () => ({
  ClienteSelector: ({
    onChange,
  }: {
    onChange: (item: { id: string; razonSocial: string } | null) => void;
  }) => (
    <button
      type="button"
      onClick={() =>
        onChange({
          id: '22222222-2222-4222-8222-222222222222',
          razonSocial: 'Cliente Demo SA',
        })
      }
    >
      Elegir cliente
    </button>
  ),
}));
vi.mock('@/features/facturacion/components/selectors/CanalVentaSelector', () => ({
  CanalVentaSelector: () => <span>Canal</span>,
}));

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

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

function renderForm() {
  const onClose = vi.fn();
  render(<NuevoPedidoManual onClose={onClose} />, { wrapper: createQueryWrapper() });
  return { onClose };
}

describe('<NuevoPedidoManual> — clase de pedido (F6.1)', () => {
  it('sin permiso de emergencia solo ofrece «Venta directa»', () => {
    setPermisos(['facturacion.pedidos.capturar']);
    renderForm();
    expect(screen.getByRole('radio', { name: /Venta directa/i })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /Emergencia/i })).not.toBeInTheDocument();
    expect(
      screen.getByText(/no se informa a A\+W/i),
    ).toBeInTheDocument();
  });

  it('con permiso, «Emergencia» pide motivo y referencia A+W opcional', () => {
    setPermisos(['facturacion.pedidos.capturar', 'facturacion.pedidos.capturar-emergencia']);
    renderForm();
    fireEvent.click(screen.getByRole('radio', { name: /Emergencia/i }));
    expect(screen.getByLabelText(/Motivo de la emergencia/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Pedido A\+W/i)).toBeInTheDocument();
    expect(screen.getByText(/Úsalo solo si A\+W no está disponible/i)).toBeInTheDocument();
    // Una emergencia no lleva subtipo.
    expect(screen.queryByRole('radio', { name: /Almacenable/i })).not.toBeInTheDocument();
  });

  it('subtipo Activo fija el comportamiento en «Venta de activo fijo»', () => {
    setPermisos(['facturacion.pedidos.capturar']);
    renderForm();
    fireEvent.click(screen.getByRole('radio', { name: /Venta directa/i }));
    fireEvent.click(screen.getByRole('radio', { name: /^Activo/i }));
    const comportamiento = screen.getByRole('textbox', { name: /Comportamiento fiscal/i });
    expect(comportamiento).toHaveValue('Venta de activo fijo');
    expect(comportamiento).toHaveAttribute('readonly');
    expect(
      screen.getByText(/Requiere autorización del Contador General antes de timbrar/i),
    ).toBeInTheDocument();

    // Al cambiar a otro subtipo se libera el comportamiento.
    fireEvent.click(screen.getByRole('radio', { name: /Servicio/i }));
    expect(screen.getByRole('combobox', { name: /Comportamiento fiscal/i })).toBeInTheDocument();
  });

  it('sin clase no envía y muestra el error', async () => {
    setPermisos(['facturacion.pedidos.capturar']);
    renderForm();
    fireEvent.click(screen.getByRole('button', { name: /Crear pedido/i }));
    expect(
      await screen.findByText(/Indica la clase del pedido/i),
    ).toBeInTheDocument();
  });

  it('envía la clasificación de una venta directa · Activo', async () => {
    setPermisos(['facturacion.pedidos.capturar']);
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/pedidos-facturables/', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          { id: 'p-1', estado: 'Importado', total: 100, version: 1 },
          { status: 201 },
        );
      }),
    );
    const { onClose } = renderForm();
    fireEvent.click(screen.getByRole('radio', { name: /Venta directa/i }));
    fireEvent.click(screen.getByRole('radio', { name: /^Activo/i }));
    fireEvent.click(screen.getByRole('button', { name: /Elegir sucursal/i }));
    fireEvent.click(screen.getByRole('button', { name: /Elegir cliente/i }));
    fireEvent.change(screen.getByPlaceholderText('Descripción'), {
      target: { value: 'Montacargas usado' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Crear pedido/i }));

    await waitFor(() => expect(onClose).toHaveBeenCalledWith({ force: true }));
    expect(body).toMatchObject({
      claseManual: 1,
      subtipoVentaDirecta: 1,
      comportamientoFiscal: 5,
      motivoEmergencia: null,
      referenciaPedidoAw: null,
    });
  });

  it('muestra en el campo el 403 de emergencia sin permiso', async () => {
    setPermisos(['facturacion.pedidos.capturar', 'facturacion.pedidos.capturar-emergencia']);
    mswServer.use(
      http.post('*/api/v1/facturacion/pedidos-facturables/', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Prohibido',
            status: 403,
            code: 'PEDIDO_EMERGENCIA_SIN_PERMISO',
          },
          { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderForm();
    fireEvent.click(screen.getByRole('radio', { name: /Emergencia/i }));
    fireEvent.change(screen.getByLabelText(/Motivo de la emergencia/i), {
      target: { value: 'A+W caído' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Elegir sucursal/i }));
    fireEvent.click(screen.getByRole('button', { name: /Elegir cliente/i }));
    fireEvent.change(screen.getByPlaceholderText('Descripción'), {
      target: { value: 'Vidrio' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Crear pedido/i }));
    expect(
      await screen.findByText(/No tienes permiso para capturar pedidos de emergencia/i),
    ).toBeInTheDocument();
  });
});
