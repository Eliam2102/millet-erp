import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SolicitarDevolucionDialog } from './SolicitarDevolucionDialog';

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('./ClienteTesoreriaSelector', () => ({
  ClienteTesoreriaSelector: ({ onChange }: { onChange: (c: { id: string }) => void }) => (
    <button type="button" onClick={() => onChange({ id: 'cli-1' })}>
      elegir cliente
    </button>
  ),
}));

const movimiento = { id: 'mov-1', monto: 1000, moneda: 'MXN', beneficiarioTipo: null, beneficiarioRef: null };

async function capturar() {
  render(<SolicitarDevolucionDialog movimiento={movimiento} abierto onCerrar={vi.fn()} />, {
    wrapper: createQueryWrapper(),
  });
  fireEvent.click(screen.getByText('elegir cliente'));
  fireEvent.change(screen.getByLabelText(/^Motivo/), { target: { value: 'Depósito duplicado' } });
  fireEvent.change(screen.getByLabelText(/^Banco destino/), { target: { value: 'BBVA' } });
  fireEvent.change(screen.getByLabelText(/^CLABE destino/), { target: { value: '032180000118359719' } });
  fireEvent.change(screen.getByLabelText(/^Titular/), { target: { value: 'Cliente SA' } });
  const boton = screen.getByRole('button', { name: 'Solicitar devolución' });
  await waitFor(() => expect(boton).toBeEnabled());
  return boton;
}

describe('<SolicitarDevolucionDialog>', () => {
  it('con campos faltantes deshabilita el botón y dice cuáles faltan', () => {
    render(<SolicitarDevolucionDialog movimiento={movimiento} abierto onCerrar={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('button', { name: 'Solicitar devolución' })).toBeDisabled();
    expect(screen.getByText(/Falta capturar el cliente/)).toBeInTheDocument();
  });

  it('envía el body correcto al endpoint del movimiento', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post('*/api/v1/tesoreria/movimientos/mov-1/solicitar-devolucion', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'dev-1' }, { status: 201 });
      }),
    );
    fireEvent.click(await capturar());
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toEqual({
      clienteId: 'cli-1',
      importe: 1000,
      motivo: 'Depósito duplicado',
      banco: 'BBVA',
      clabe: '032180000118359719',
      titular: 'Cliente SA',
    });
  });

  it('muestra el detail de un 422', async () => {
    mswServer.use(
      http.post('*/api/v1/tesoreria/movimientos/mov-1/solicitar-devolucion', () =>
        HttpResponse.json(
          { title: 'Regla de negocio', status: 422, code: 'DEV_DEPOSITO_CONFIRMADO', detail: 'El depósito ya se confirmó contra una propuesta de Crédito y Cobranza.' },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        )),
    );
    fireEvent.click(await capturar());
    expect(await screen.findByText(/ya se confirmó contra una propuesta/)).toBeInTheDocument();
  });
});
