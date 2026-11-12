import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { RoturasAppPorAutorizar } from '@/features/almacen/components/RoturasAppPorAutorizar';

const h = vi.hoisted(() => ({ mutate: vi.fn() }));
const payload = JSON.stringify({
  datos: { sucursal: 'CIRCUITO', motivo: 'Caída', piezas: 2, almacenes: [{ grupos: [{ articuloAw: 'VCL06', ancho: 100, alto: 200, lote: 'L1', piezas: 2 }] }] },
});
vi.mock('@/features/almacen/api/useSalidasRotura', () => ({
  useRoturasApp: () => ({ data: [{ mensajeId: 'm-1', referencia: 'MV-1', folio: null, recibidoAt: '2026-11-10T10:00:00Z', payload }], isError: false }),
  useDecidirRoturaApp: () => ({ mutate: h.mutate, isPending: false }),
}));

beforeEach(() => vi.clearAllMocks());

describe('RoturasAppPorAutorizar', () => {
  it('resume el mensaje y autoriza con el mensajeId', () => {
    render(<RoturasAppPorAutorizar />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('MV-1')).toBeInTheDocument();
    expect(screen.getByText('CIRCUITO')).toBeInTheDocument();
    expect(screen.getByText(/VCL06 · 100×200 · lote L1 · 2 pzas/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Autorizar rotura de la app/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar rotura' }));
    expect(h.mutate).toHaveBeenCalledWith({ mensajeId: 'm-1', accion: 'autorizar', motivo: undefined }, expect.anything());
  });

  it('rechazar sin motivo no manda la decisión', () => {
    render(<RoturasAppPorAutorizar />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /Rechazar rotura de la app/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar rotura' }));
    expect(h.mutate).not.toHaveBeenCalled();
  });
});
