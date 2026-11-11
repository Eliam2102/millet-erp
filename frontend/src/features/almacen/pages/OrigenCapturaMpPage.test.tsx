import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { OrigenCapturaMpPage } from '@/features/almacen/pages/OrigenCapturaMpPage';
import type { OrigenCapturaMpResponse } from '@/features/almacen/api/useOrigenCapturaMp';

const hook = vi.hoisted(() => ({ useOrigenCapturaMp: vi.fn(), mutate: vi.fn() }));
vi.mock('@/features/almacen/api/useOrigenCapturaMp', () => ({
  useOrigenCapturaMp: hook.useOrigenCapturaMp,
  useCambiarOrigenCapturaMp: () => ({ mutate: hook.mutate, isPending: false }),
}));

const datos: OrigenCapturaMpResponse = {
  eventos: [
    { evento: 'recepcion', nombre: 'recepción', origen: 'APP', desde: '2026-11-13T14:00:00Z', pendientes: { mensajesEnRevision: 0, mensajesSinAcuse: 0 } },
    { evento: 'consumo', nombre: 'consumo', origen: 'AMBOS', desde: null, pendientes: { mensajesEnRevision: 1, mensajesSinAcuse: null } },
    { evento: 'merma', nombre: 'merma', origen: 'AMBOS', desde: null, pendientes: { mensajesEnRevision: 0, mensajesSinAcuse: null } },
    { evento: 'requisicion', nombre: 'requisición', origen: 'ERP', desde: '2026-11-12T14:00:00Z', pendientes: { mensajesEnRevision: 0, mensajesSinAcuse: 0 } },
  ],
  bitacora: [
    { id: 'c1', evento: 'recepcion', origenAnterior: 'AMBOS', origen: 'APP', motivo: 'Arranque de la app el 13-nov', cambiadoPor: 'Jefe de Almacén Conkal', cambiadoAt: '2026-11-13T14:00:00Z' },
  ],
};

beforeEach(() => vi.clearAllMocks());

describe('OrigenCapturaMpPage', () => {
  it('muestra el origen vigente de cada evento, sus pendientes y la bitácora', () => {
    hook.useOrigenCapturaMp.mockReturnValue({ data: datos, isLoading: false, isError: false, refetch: vi.fn() });
    render(<OrigenCapturaMpPage />, { wrapper: createQueryWrapper() });
    expect(screen.getAllByText('App de Almacenes').length).toBeGreaterThan(0);
    expect(screen.getByText('Regresar al ERP')).toBeInTheDocument();
    expect(screen.getByText('1 en revisión')).toBeInTheDocument();
    expect(screen.getByText('Arranque de la app el 13-nov')).toBeInTheDocument();
  });

  it('pide motivo y manda el cambio del evento con su destino', () => {
    hook.useOrigenCapturaMp.mockReturnValue({ data: datos, isLoading: false, isError: false, refetch: vi.fn() });
    render(<OrigenCapturaMpPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getAllByRole('button', { name: 'Pasar a la app' })[1]); // merma (consumo es el [0])
    fireEvent.change(screen.getByLabelText('Motivo del cambio'), { target: { value: 'Arranque 13-nov' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Pasar a la app' }).at(-1)!);
    expect(hook.mutate).toHaveBeenCalledWith(
      { evento: 'merma', origen: 'APP', motivo: 'Arranque 13-nov' },
      expect.anything(),
    );
  });
});
