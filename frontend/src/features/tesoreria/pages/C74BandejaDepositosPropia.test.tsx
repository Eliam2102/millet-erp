import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { DepositoConfirmacionResponse } from '@/features/tesoreria/api/types';

// C7.4 · R12: quien propuso el cobro en CxC no puede confirmarlo; la bandeja
// deshabilita «Confirmar» con aviso (el backend sigue respondiendo DEP_MISMO_USUARIO).

const USUARIO = '11111111-1111-4111-8111-111111111111';

function deposito(id: string, propuestoPor: string): DepositoConfirmacionResponse {
  return {
    id,
    propuestaCxcId: `prop-${id}`,
    cajaSesionId: null,
    clienteId: 'cli-1',
    clienteClave: 'C001',
    clienteRazonSocial: `Cliente ${id}`,
    depositoRef: `SPEI-${id}`,
    montoEsperado: 50_000,
    moneda: 'MXN',
    estado: 1,
    motivoRechazo: null,
    reppTimbrado: false,
    movimientoId: null,
    facturas: [],
    resueltaPor: null,
    resueltaEn: null,
    recibidoEn: '2026-10-26T12:00:00Z',
    version: 1,
    propuestoPor,
  } as DepositoConfirmacionResponse;
}

vi.mock('@/features/tesoreria/api/useTesoreria', () => ({
  useDepositos: () => ({
    data: { items: [deposito('propio', USUARIO), deposito('ajeno', '99999999-9999-4999-8999-999999999999')], offset: 0, limit: 200, total: 2 },
    isLoading: false,
    isError: false,
  }),
  useRechazarDeposito: () => ({ mutate: vi.fn(), isPending: false }),
  useDepositosSinIdentificar: () => ({ data: [], isLoading: false, isError: false }),
}));
vi.mock('@/features/cxc/components/nueva-propuesta-context', () => ({
  useNuevaPropuesta: () => ({ abrir: vi.fn(), cerrar: () => {}, setDirty: () => {} }),
}));
vi.mock('@/features/tesoreria/components/ConfirmarDepositoDialog', () => ({ ConfirmarDepositoDialog: () => null }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

import { useAuthStore } from '@/lib/auth/auth-store';
import { BandejaDepositos } from './BandejaDepositos';

describe('Depósitos por confirmar · R12 (C7.4)', () => {
  beforeEach(() => {
    useAuthStore.setState({ user: { id: USUARIO, email: 'p@e.com', nombre: 'Proponente' } });
  });

  it('deshabilita «Confirmar» en la propuesta propia con aviso y lo deja activo en la ajena', () => {
    render(<BandejaDepositos />);

    const botones = screen.getAllByRole('button', { name: 'Confirmar' });
    expect(botones).toHaveLength(2);
    const propio = botones.find((b) => b.hasAttribute('disabled'));
    expect(propio).toBeDefined();
    expect(propio).toHaveAccessibleDescription('No puedes confirmar una propuesta que tú hiciste');
    expect(propio!.parentElement).toHaveAttribute('title', 'No puedes confirmar una propuesta que tú hiciste');
    expect(botones.filter((b) => !b.hasAttribute('disabled'))).toHaveLength(1);
  });
});
