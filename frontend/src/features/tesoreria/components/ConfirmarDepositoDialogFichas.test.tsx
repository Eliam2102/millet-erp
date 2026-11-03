import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { DepositoConfirmacionResponse } from '@/features/tesoreria/api/types';

// 86e3vr1tr: corte de caja con 1..N fichas — saldo por depositar, historial y
// documento soporte obligatorio desde la 2ª ficha.

vi.mock('@/features/tesoreria/api/useTesoreria', () => ({
  useConfirmarDeposito: () => ({ mutate: vi.fn(), isPending: false }),
  useMovimientos: () => ({ data: { items: [] }, isLoading: false }),
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('@tanstack/react-query', async (orig) => ({
  ...(await orig<typeof import('@tanstack/react-query')>()),
  useQueryClient: () => ({ invalidateQueries: vi.fn() }),
}));

import { ConfirmarDepositoDialog } from './ConfirmarDepositoDialog';

function corte(fichas: number): DepositoConfirmacionResponse {
  return {
    id: 'c1', propuestaCxcId: null, cajaSesionId: 'ses-1', clienteId: null, clienteClave: null,
    clienteRazonSocial: null, depositoRef: 'CAJA 2026-11-02', montoEsperado: 4200, moneda: 'MXN',
    estado: fichas === 0 ? 1 : 4, motivoRechazo: null, reppTimbrado: false, movimientoId: null,
    facturas: [], resueltaPor: null, resueltaEn: null, recibidoEn: '2026-11-02T20:00:00Z', version: 1,
    saldoPorDepositar: 4200 - fichas * 2500,
    fichas: Array.from({ length: fichas }, (_, i) => ({
      id: `f${i}`, movimientoId: `m${i}`, importe: 2500, fechaFicha: '2026-11-02', numeroFicha: 'F-1',
      soporteTipo: null, soporteFolio: null, confirmadaEn: '2026-11-02T21:00:00Z',
    })),
  } as DepositoConfirmacionResponse;
}

describe('<ConfirmarDepositoDialog> corte de caja', () => {
  it('primera ficha: botón «Agregar ficha», sin documento soporte', () => {
    render(<ConfirmarDepositoDialog deposito={corte(0)} onOpenChange={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Agregar ficha' })).toBeDisabled();
    expect(screen.queryByLabelText('Documento soporte')).not.toBeInTheDocument();
  });

  it('segunda ficha: muestra el saldo, el historial y exige vale de caja o corte parcial con folio', () => {
    render(<ConfirmarDepositoDialog deposito={corte(1)} onOpenChange={vi.fn()} />);
    expect(screen.getByText(/Saldo por depositar/)).toBeInTheDocument();
    expect(screen.getByText('Fichas del corte')).toBeInTheDocument();
    expect(screen.getByLabelText('Documento soporte')).toBeInTheDocument();
    expect(screen.getByLabelText(/Folio del soporte/)).toBeInTheDocument();
  });
});
