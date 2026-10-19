import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EstadoDevolucionCliente, OrigenDevolucionCliente, type DevolucionClienteResponse } from '@/features/tesoreria/api/types';
import { PagarDevolucionDialog } from './PagarDevolucionDialog';

vi.mock('./CuentaBancariaSelector', () => ({
  CuentaBancariaSelector: () => <div data-testid="selector-cuenta" />,
}));

function devolucion(origen: OrigenDevolucionCliente): DevolucionClienteResponse {
  return {
    id: 'dev-1', origen, estado: EstadoDevolucionCliente.Autorizada, clienteId: 'cli-1',
    clienteClave: 'C1', clienteRazonSocial: 'Cliente SA', importe: 500, importeMaximoOrigen: 1000,
    disponibleOrigen: 500, moneda: 'MXN', motivo: 'x', movimientoOrigenId: null, notaCreditoId: null,
    notaCreditoFolio: null, notaCreditoUuid: null, destinoBanco: 'BBVA', destinoClabe: '032180000118359719',
    destinoTitular: 'Cliente SA', solicitadaPor: null, solicitadaEn: '2026-10-17T00:00:00Z', autorizadaPor: null,
    autorizadaEn: null, motivoRechazo: null, motivoCancelacion: null, movimientoPagoId: null, pagadaEn: null,
    sucursalId: null, version: 3,
  } as DevolucionClienteResponse;
}

function abrir(origen: OrigenDevolucionCliente) {
  render(<PagarDevolucionDialog devolucion={devolucion(origen)} abierto onCerrar={vi.fn()} />, {
    wrapper: createQueryWrapper(),
  });
}

describe('<PagarDevolucionDialog>', () => {
  it('origen NC muestra el selector de cuenta y no deja pagar sin cuenta', () => {
    abrir(OrigenDevolucionCliente.NotaCredito);
    expect(screen.getByTestId('selector-cuenta')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText(/Verifiqué en la banca/));
    expect(screen.getByRole('button', { name: 'Registrar pago' })).toBeDisabled();
    expect(screen.getByText('Selecciona la cuenta de egreso')).toBeInTheDocument();
  });

  it('origen depósito no muestra selector y envía fondosVerificados sin cuentaBancariaId', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post('*/api/v1/tesoreria/devoluciones/dev-1/pagar', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'dev-1' });
      }),
    );
    abrir(OrigenDevolucionCliente.DepositoErroneo);
    expect(screen.queryByTestId('selector-cuenta')).not.toBeInTheDocument();
    const boton = screen.getByRole('button', { name: 'Registrar pago' });
    expect(boton).toBeDisabled();
    fireEvent.click(screen.getByLabelText(/Verifiqué en la banca/));
    await waitFor(() => expect(boton).toBeEnabled());
    fireEvent.click(boton);
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ fondosVerificados: true });
    expect(body).not.toHaveProperty('cuentaBancariaId');
  });
});
