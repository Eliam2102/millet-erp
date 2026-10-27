import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { DepositoPorIdentificarItem, FacturaAbiertaItem } from '@/features/cxc/api/types';

// F1-CXC-03 · C7.4: pestaña «Sin identificar» + Identificar prellenado (CA7.9),
// «Sugerir» por antigüedad sin enviar (V61), días en espera y parcialidad/saldo insoluto (C7.4-a).

const deposito: DepositoPorIdentificarItem = {
  movimientoBancarioId: 'mov-50k',
  cuenta: 'BBVA ****6789',
  fechaValor: '2026-10-26',
  referencia: 'SPEI-50K',
  monto: 50_000,
  disponible: 50_000,
  moneda: 'MXN',
  sucursalId: null,
  clienteId: '33333333-3333-4333-8333-333333333333',
  clienteNombre: 'ACME SA',
};

function factura(uuid: string, folio: string, saldo: number, vence: string): FacturaAbiertaItem {
  return {
    facturaCarteraId: `fc-${uuid}`,
    uuid,
    folio,
    moneda: 'MXN',
    metodoPago: 'PPD',
    total: saldo,
    montoPagado: 0,
    montoNc: 0,
    saldo,
    fechaTimbrado: '2026-07-01',
    fechaVencimiento: vence,
  };
}

const mocks = vi.hoisted(() => ({
  search: {} as Record<string, unknown>,
  abrir: vi.fn(),
  mutate: vi.fn(),
  propuesta: null as unknown,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
  useNavigate: () => () => {},
  useSearch: () => mocks.search,
  useParams: () => ({ id: 'pap-1' }),
}));
vi.mock('@/features/cxc/components/nueva-propuesta-context', () => ({
  useNuevaPropuesta: () => ({ abrir: mocks.abrir, cerrar: () => {}, setDirty: () => {} }),
}));
vi.mock('@/features/cxc/components/ClienteSelectorCxc', () => ({
  ClienteSelectorCxc: ({ value }: { value: string | null }) => <span>cliente:{value}</span>,
}));
vi.mock('@/features/cxc/api/useLineasCredito', () => ({
  useClientesLookupCxc: () => ({ data: [{ id: 'cli-1', razonSocial: 'ACME SA' }] }),
}));
vi.mock('@/features/cxc/api/useAplicaciones', () => ({
  usePropuestasAplicacion: () => ({ data: { items: [], offset: 0, limit: 200, total: 0 }, isLoading: false, isError: false }),
  useDepositosPorIdentificar: () => ({ data: [deposito], isLoading: false, isError: false }),
  usePropuestaAplicacion: () => ({ data: mocks.propuesta, isLoading: false, isError: false }),
  useFacturasAbiertas: () => ({
    data: [
      { ...factura('u-c', 'FV-3', 30_000, '2026-09-30'), numParcialidadSiguiente: 2 },
      factura('u-a', 'FV-1', 20_000, '2026-08-01'),
      factura('u-b', 'FV-2', 20_000, '2026-09-01'),
    ],
    isLoading: false,
    isError: false,
    error: null,
    refetch: () => {},
  }),
  useToleranciasNoFiscal: () => ({ data: { MXN: 1000 }, isSuccess: true, isError: false }),
  useCrearPropuestaAplicacion: () => ({ mutate: mocks.mutate, isPending: false }),
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

import { BandejaAplicaciones } from '@/features/cxc/pages/BandejaAplicaciones';
import { NuevaPropuestaAplicacion } from '@/features/cxc/pages/NuevaPropuestaAplicacion';
import { DetalleAplicacion } from '@/features/cxc/pages/DetalleAplicacion';

beforeEach(() => {
  vi.clearAllMocks();
  mocks.search = {};
});

describe('C7.4 · depósitos sin identificar', () => {
  it('la pestaña «Sin identificar» lista el depósito e «Identificar» abre la propuesta con él', () => {
    mocks.search = { vista: 'sin-identificar' };
    render(<BandejaAplicaciones />);

    expect(screen.getByRole('tab', { name: /Sin identificar/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('SPEI-50K')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Días en espera' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Identificar depósito SPEI-50K/ }));

    expect(mocks.abrir).toHaveBeenCalledWith(deposito);
  });

  it('la nueva propuesta llega prellenada, lista de la más antigua a la más reciente, sugiere sin enviar y manda el movimiento sin parcialidad', async () => {
    render(<NuevaPropuestaAplicacion onClose={() => {}} deposito={deposito} />);

    expect(screen.getByTestId('deposito-identificado')).toHaveTextContent('SPEI-50K');
    expect(screen.getByLabelText(/Referencia del depósito/)).toHaveValue('SPEI-50K');
    expect(screen.getByLabelText(/Monto del depósito/)).toHaveValue(50_000);
    expect(screen.getByText('cliente:33333333-3333-4333-8333-333333333333')).toBeInTheDocument();

    // V61: facturas de la más antigua a la más reciente.
    const folios = screen.getAllByLabelText(/^Importe aplicado a /).map((e) => e.getAttribute('aria-label'));
    expect(folios).toEqual(['Importe aplicado a FV-1', 'Importe aplicado a FV-2', 'Importe aplicado a FV-3']);

    fireEvent.click(screen.getByRole('button', { name: 'Sugerir' }));
    expect(mocks.mutate).not.toHaveBeenCalled(); // «Sugerir» solo llena importes; no envía.
    expect(screen.getByLabelText('Importe aplicado a FV-1')).toHaveValue(20_000);
    expect(screen.getByLabelText('Importe aplicado a FV-2')).toHaveValue(20_000);
    expect(screen.getByLabelText('Importe aplicado a FV-3')).toHaveValue(10_000);
    // FV-3 queda como parcialidad: el matching muestra el saldo insoluto.
    expect(screen.getByText(/Parc\. 2 · saldo insoluto/)).toHaveTextContent('20,000');

    // R13: sin remittance no se propone.
    expect(screen.getByRole('button', { name: 'Proponer aplicación' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Remittance del cliente/), { target: { value: 'REM-ACME-77' } });
    fireEvent.click(screen.getByRole('button', { name: 'Proponer aplicación' }));

    await waitFor(() => expect(mocks.mutate).toHaveBeenCalled());
    const { command } = mocks.mutate.mock.calls[0][0];
    expect(command.movimientoBancarioId).toBe('mov-50k');
    expect(command.montoDeposito).toBe(50_000);
    expect(command.facturas).toHaveLength(3);
    expect(command.facturas[0]).not.toHaveProperty('numParcialidad');
  });

  it('el detalle muestra la parcialidad y el saldo insoluto por factura', () => {
    mocks.propuesta = {
      id: 'pap-1',
      clienteId: 'cli-1',
      depositoRef: 'SPEI-50K',
      montoDeposito: 10_000,
      moneda: 'MXN',
      remittanceRef: 'REM-ACME-77',
      ajusteNoFiscal: 0,
      estado: 1,
      motivoRechazo: null,
      resueltaPor: null,
      resueltaEn: null,
      version: 1,
      facturas: [
        {
          facturaCarteraId: 'fc-u-c',
          facturaUuid: 'u-c',
          folio: 'FV-3',
          importeAplicado: 10_000,
          numParcialidad: 2,
          saldoInsoluto: 20_000,
        },
      ],
    };
    render(<DetalleAplicacion />);
    expect(screen.getByText(/Parc\. 2/)).toHaveTextContent('saldo insoluto');
    expect(screen.getByText(/Parc\. 2/)).toHaveTextContent('20,000');
  });
});
