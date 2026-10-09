import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ConceptoSelector } from './ConceptoSelector';
import { ReclasificarMovimiento } from './ReclasificarMovimiento';
import { DetalleAplicacion } from '@/features/cxc/pages/DetalleAplicacion';
import type { MovimientoBancarioResponse } from '../api/types';

const mocks = vi.hoisted(() => ({ mutate: vi.fn(), propuesta: { estado: 1, saldoAFavorPorIdentificar: 100 } }));
vi.mock('../api/useTesoreria', () => ({
  useConceptos: () => ({ data: [{ id: 'concepto-1', nombre: 'Cobro de cliente', clasificacionFlujo: 1, activo: true, esEjemplo: true }], isLoading: false }),
  useReclasificarMovimiento: () => ({ mutate: mocks.mutate, isPending: false }),
}));
vi.mock('@/lib/api', () => ({ esApiError: () => false, useBodyScopedIdempotencyKey: () => () => 'p5-test' }));
vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'propuesta-1' }), useSearch: () => ({}),
  Link: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));
vi.mock('@/features/cxc/api/useAplicaciones', () => ({ usePropuestaAplicacion: () => ({ data: {
  ...mocks.propuesta, id: 'propuesta-1', clienteId: 'cliente-1', depositoRef: 'DEP-P5', montoDeposito: 1100,
  moneda: 'MXN', remittanceRef: 'REM-P5', ajusteNoFiscal: 0, facturas: [], motivoRechazo: 'No existe depósito',
} }) }));
vi.mock('@/features/cxc/api/useLineasCredito', () => ({ useClientesLookupCxc: () => ({ data: [{ razonSocial: 'Cliente ficticio P5' }] }) }));

const movimiento = { id: 'movimiento-1', conceptoId: 'concepto-1', version: 3 } as MovimientoBancarioResponse;

describe('Controles de P5', () => {
  beforeEach(() => { vi.clearAllMocks(); mocks.propuesta.estado = 1; });
  it('ofrece selector de concepto con etiqueta accesible', () => {
    render(<ConceptoSelector value={null} onChange={vi.fn()} />);
    expect(screen.getByRole('combobox', { name: 'Concepto de flujo de efectivo' })).toBeInTheDocument();
  });
  it('reclasifica con concepto, motivo y versión del movimiento', () => {
    render(<ReclasificarMovimiento movimiento={movimiento} />);
    expect(screen.getByRole('button', { name: 'Reclasificar' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo de la reclasificación'), { target: { value: '  Corrección P5  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reclasificar' }));
    expect(mocks.mutate).toHaveBeenCalledWith({ movimientoId: 'movimiento-1', conceptoId: 'concepto-1', motivo: 'Corrección P5', version: 3, idempotencyKey: 'p5-test' }, expect.any(Object));
  });
  it('CxC muestra excedente pendiente y carece de acciones de confirmar o rechazar', () => {
    render(<DetalleAplicacion />);
    expect(screen.getByText(/Saldo a favor por identificar/)).toHaveTextContent('Pendiente de confirmación bancaria');
    expect(screen.queryByRole('button', { name: /confirmar|rechazar/i })).not.toBeInTheDocument();
  });
  it('una propuesta rechazada no se presenta como saldo a favor vigente', () => {
    mocks.propuesta.estado = 3;
    render(<DetalleAplicacion />);
    expect(screen.queryByText(/Saldo a favor por identificar/)).not.toBeInTheDocument();
  });
});
