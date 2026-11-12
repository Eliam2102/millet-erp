import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ApiError } from '@/lib/api/error';
import { TrasladarPosicionSheet } from '@/features/almacen/components/TrasladarPosicionSheet';
import type { SaldoUbicacionItem } from '@/features/almacen/api/types';

const h = vi.hoisted(() => ({ mutate: vi.fn(), toastError: vi.fn() }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: h.toastError } }));
vi.mock('@/features/almacen/api/useSaldosCierreReportes', () => ({ useTrasladarPosicion: () => ({ mutate: h.mutate, isPending: false }) }));
vi.mock('@/components/erp/selectors/UbicacionBinSelector', () => ({
  UbicacionBinSelector: ({ onChange }: { onChange: (id: string) => void }) => <button type="button" onClick={() => onChange('u-dest')}>Elegir destino</button>,
}));

const fila: SaldoUbicacionItem = {
  ubicacionId: 'u-1', clave: 'R-01', nombre: 'Rack 1', subAlmacenClave: 'SUB', almacenClave: 'ALM', esDefault: false,
  cantidad: 20, cantidadDisponible: 20, costoPromedioMxn: 100, productoAwVarianteId: 'v-1', anchoMm: 1000, altoMm: 2000,
  areaHojaM2: 2, lote: 'L1', saldoId: 's-1', version: '639000000000000123',
};
const onRecargar = vi.fn();
const montar = () => render(<TrasladarPosicionSheet articuloId="a-1" fila={fila} onOpenChange={vi.fn()} onRecargar={onRecargar} />, { wrapper: createQueryWrapper() });
function llenar() {
  fireEvent.change(screen.getByLabelText(/Hojas a trasladar/), { target: { value: '2' } });
  fireEvent.click(screen.getByRole('button', { name: 'Elegir destino' }));
  fireEvent.click(screen.getByRole('button', { name: 'Trasladar' }));
}

beforeEach(() => vi.clearAllMocks());

describe('TrasladarPosicionSheet', () => {
  it('manda saldoId y version de la fila con la cantidad y el destino', () => {
    montar();
    llenar();
    expect(h.mutate).toHaveBeenCalledWith(
      { command: { saldoId: 's-1', version: '639000000000000123', cantidad: 2, ubicacionDestinoId: 'u-dest', motivo: null }, idempotencyKey: expect.any(String) },
      expect.anything(),
    );
  });

  it('no manda con media hoja ni sin destino', () => {
    montar();
    fireEvent.change(screen.getByLabelText(/Hojas a trasladar/), { target: { value: '1.5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Trasladar' }));
    expect(screen.getByText('Se trasladan hojas enteras.')).toBeInTheDocument();
    expect(h.mutate).not.toHaveBeenCalled();
  });

  it('409 TRASLADO_SALDO_CAMBIO avisa que recargue y vuelve a leer', () => {
    h.mutate.mockImplementationOnce((_a: unknown, o: { onError: (e: unknown) => void }) =>
      o.onError(new ApiError({ type: 'x', title: 'c', status: 409, code: 'TRASLADO_SALDO_CAMBIO' }, 409)));
    montar();
    llenar();
    expect(h.toastError).toHaveBeenCalledWith('La existencia cambió, recarga.', expect.anything());
    expect(onRecargar).toHaveBeenCalled();
  });
});
