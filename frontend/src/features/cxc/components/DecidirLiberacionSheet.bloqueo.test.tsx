import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ComportamientoSerie, EstadoLineaCredito } from '@/features/cxc/api/types';
import { DecidirLiberacionSheet } from './DecidirLiberacionSheet';

const state = vi.hoisted(() => ({ lineas: [] as unknown[] }));

vi.mock('@/features/cxc/api/useLiberaciones', () => ({
  useDecidirLiberacion: () => ({ mutate: vi.fn(), isPending: false }),
  useAutorizacionesCredito: () => ({ data: { items: [] } }),
  useReglasLiberacionSerie: () => ({ data: [{ prefijo: '50', comportamiento: ComportamientoSerie.SiempreLibera }] }),
}));
vi.mock('@/features/cxc/api/useLineasCredito', () => ({
  useCreditoDisponible: (clienteId: string | null) => ({
    data: clienteId ? { clienteId, lineas: state.lineas, datoIncompleto: false } : undefined,
    isLoading: false,
  }),
}));
vi.mock('@/features/cxc/components/ClienteSelectorCxc', () => ({
  ClienteSelectorCxc: ({ onChange }: { onChange: (i: { id: string; razonSocial: string }) => void }) => (
    <button type="button" onClick={() => onChange({ id: 'c-1', razonSocial: 'Cliente FIX' })}>
      elegir cliente
    </button>
  ),
}));

const linea = (estado: EstadoLineaCredito, bloqueoAutomatico: boolean) => ({
  lineaCreditoId: 'l-1', moneda: 'MXN', limite: 100_000, facturado: 0, liberadoSinFactura: 0, disponible: 100_000,
  estado, origen: 1, plazoDias: 30, bloqueoAutomatico,
});

function capturar() {
  render(<DecidirLiberacionSheet open onOpenChange={() => {}} />);
  fireEvent.change(screen.getByLabelText(/Folio del pedido/), { target: { value: '50047568' } });
  fireEvent.change(screen.getByLabelText(/Monto del pedido/), { target: { value: '5000' } });
  fireEvent.click(screen.getByRole('button', { name: 'elegir cliente' }));
}

beforeEach(() => {
  state.lineas = [];
});

// R2 (Crédito y Cobranza, 9-nov): la serie que siempre libera se retiene si el cliente está bloqueado por vencimiento.
describe('<DecidirLiberacionSheet> · serie que siempre libera con cliente bloqueado', () => {
  it('bloqueado por vencimiento: explica que solo Crédito y Cobranza la libera y el botón registra retención', () => {
    state.lineas = [linea(EstadoLineaCredito.Bloqueada, true)];
    capturar();

    expect(screen.getByText(/bloqueado por vencimiento: esta serie no se libera sola/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar retención' })).toBeInTheDocument();
  });

  it('sin bloqueo por vencimiento la serie se libera como siempre', () => {
    state.lineas = [linea(EstadoLineaCredito.Activa, false)];
    capturar();

    expect(screen.queryByText(/bloqueado por vencimiento/)).toBeNull();
    expect(screen.getByRole('button', { name: 'Liberar pedido' })).toBeInTheDocument();
  });
});
