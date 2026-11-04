import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { createQueryWrapper } from '@/test/test-query-client';
import { RegistrarSalidaDevolucionSheet } from './DevolucionAccionesDialogs';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/components/erp/selectors/UbicacionBinSelector', () => ({
  UbicacionBinSelector: ({ onChange }: { onChange: (id: string) => void }) => (
    <button type="button" onClick={() => onChange('bin-1')}>
      elegir rack
    </button>
  ),
}));
vi.mock('@/features/almacen/api', async (importOriginal) => {
  const real = await importOriginal<typeof import('@/features/almacen/api')>();
  return {
    ...real,
    useSubAlmacenes: () => ({
      isLoading: false,
      data: { items: [{ id: '22222222-2222-4222-8222-222222222222', clave: 'INSUMOS', nombre: 'Insumos' }] },
    }),
    useDevolucionProveedor: () => ({
      isLoading: false,
      data: { lineas: [{ id: 'l-1', articuloId: 'a-1', articuloClave: 'AMC20509', cantidad: 1, unidadMedida: 'PZA' }] },
    }),
    // Respuesta con la forma real del API (RegistrarSalidaDevolucionAProveedorResponse).
    useRegistrarSalidaDevolucionProveedor: () => ({
      isPending: false,
      mutate: (_: unknown, opts: { onSuccess: (r: unknown) => void }) =>
        opts.onSuccess({ devolucionId: 'd-1', movimientoSalidaId: 'm-1', folioSalida: 'M-DEV2026-000004' }),
    }),
  };
});

describe('<RegistrarSalidaDevolucionSheet>', () => {
  it('el toast muestra el folio de la salida (F-ALM-01)', async () => {
    render(<RegistrarSalidaDevolucionSheet devolucionId="d-1" open onOpenChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.keyDown(screen.getByRole('combobox', { name: /Sub-almacén origen/ }), { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: 'INSUMOS · Insumos' }));
    fireEvent.click(screen.getByRole('button', { name: 'elegir rack' }));
    fireEvent.click(screen.getByRole('button', { name: 'Registrar salida' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Salida M-DEV2026-000004 registrada'));
  });
});
