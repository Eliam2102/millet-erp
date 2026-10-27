import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { DepositoConfirmacionResponse } from '@/features/tesoreria/api/types';

// C7.4 (ficha 26-oct): la bandeja «Sin identificar» también la ve Tesorería
// (permiso de lectura de movimientos); «Identificar» solo con permiso de proponer
// de CxC. E8: una propuesta ligada se confirma solo contra su movimiento.

const sinIdentificar = {
  movimientoBancarioId: 'mov-9001',
  cuenta: 'BANCO DEMO 01 ****0001',
  fechaValor: '2026-10-15',
  referencia: 'TRF-DEMO-9001',
  monto: 50_000,
  disponible: 30_000,
  moneda: 'MXN',
  sucursalId: null,
  clienteId: 'cli-demo',
  clienteNombre: 'CLIENTE DEMO CXC-A',
};

const mocks = vi.hoisted(() => ({ permisos: new Set<string>(), abrir: vi.fn() }));
vi.mock('@/features/tesoreria/api/useTesoreria', () => ({
  useDepositos: () => ({ data: { items: [], offset: 0, limit: 200, total: 0 }, isLoading: false, isError: false }),
  useRechazarDeposito: () => ({ mutate: vi.fn(), isPending: false }),
  useDepositosSinIdentificar: () => ({ data: [sinIdentificar], isLoading: false, isError: false }),
  useConfirmarDeposito: () => ({ mutate: vi.fn(), isPending: false }),
  useMovimientos: () => ({
    data: { items: [{ id: 'mov-ligado', fechaValor: '2026-10-15', monto: 50_000, moneda: 'MXN', referenciaBancaria: 'TRF', estadoAplicacion: 1 }] },
    isLoading: false,
  }),
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: (p: string) => mocks.permisos.has(p) }));
vi.mock('@/features/cxc/components/nueva-propuesta-context', () => ({
  useNuevaPropuesta: () => ({ abrir: mocks.abrir, cerrar: () => {}, setDirty: () => {} }),
}));
vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  useBodyScopedIdempotencyKey: () => () => 'k',
}));

import { BandejaDepositos } from './BandejaDepositos';
import { ConfirmarDepositoDialog } from '@/features/tesoreria/components/ConfirmarDepositoDialog';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

function conCliente(ui: React.ReactElement) {
  return render(<QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>);
}

function abrirSinIdentificar() {
  conCliente(<BandejaDepositos />);
  fireEvent.mouseDown(screen.getByRole('tab', { name: /Sin identificar/ }), { button: 0 });
}

describe('Tesorería · Sin identificar (C7.4)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.permisos = new Set(['tesoreria.depositos.confirmar', 'tesoreria.movimientos.ver']);
  });

  it('Tesorería ve la pestaña con días en espera y sin «Identificar» si no puede proponer', () => {
    abrirSinIdentificar();
    expect(screen.getByText('TRF-DEMO-9001')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Días en espera' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Identificar/ })).not.toBeInTheDocument();
  });

  it('con permiso de proponer de CxC, «Identificar» abre la propuesta con el depósito', () => {
    mocks.permisos.add('cuentas_por_cobrar.aplicacion-pago.proponer');
    abrirSinIdentificar();
    fireEvent.click(screen.getByRole('button', { name: /Identificar depósito TRF-DEMO-9001/ }));
    expect(mocks.abrir).toHaveBeenCalledWith(sinIdentificar);
  });

  it('sin permiso de lectura de movimientos no hay pestaña', () => {
    mocks.permisos = new Set(['tesoreria.depositos.confirmar']);
    conCliente(<BandejaDepositos />);
    expect(screen.queryByRole('tab', { name: /Sin identificar/ })).not.toBeInTheDocument();
  });

  it('E8: la propuesta ligada fija el movimiento y no ofrece otro ni alta rápida', () => {
    mocks.permisos.add('tesoreria.movimientos.registrar');
    const deposito = {
      id: 'dep-1', propuestaCxcId: 'prop-1', cajaSesionId: null, clienteId: 'cli-demo', clienteClave: null,
      clienteRazonSocial: null, depositoRef: 'TRF-DEMO-9001', montoEsperado: 50_000, moneda: 'MXN', estado: 1,
      motivoRechazo: null, reppTimbrado: false, movimientoId: null, facturas: [], resueltaPor: null, resueltaEn: null,
      recibidoEn: '2026-10-26T12:00:00Z', version: 1, movimientoSugeridoId: 'mov-ligado',
    } as DepositoConfirmacionResponse;
    render(
      <QueryClientProvider client={new QueryClient()}>
        <ConfirmarDepositoDialog deposito={deposito} onOpenChange={() => {}} />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('combobox', { name: 'Movimiento de ingreso' })).toBeDisabled();
    expect(screen.getByText(/ligada al ingreso que CxC identificó/)).toBeInTheDocument();
    expect(screen.queryByText(/alta rápida/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirmar depósito' })).toBeEnabled();
  });
});
