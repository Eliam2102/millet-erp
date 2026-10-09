import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import { RegistrarCobroCard } from './RegistrarCobroCard';

const registrar = vi.hoisted(() => vi.fn());
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/features/facturacion/api/useCajaSesiones', () => ({
  useSesionActual: () => ({ data: { sesion: { estado: 'Abierta' }, diaAnteriorPendiente: false } }),
  useRegistrarCobro: () => ({ mutate: registrar, isPending: false }),
}));

describe('CA1.10 · cobro en pantalla', () => {
  it('ofrece solo el catálogo activo y bloquea un cobro si desactivan su selección', async () => {
    let activa = true;
    mswServer.use(
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([
          { id: '01', claveSat: '01', descripcion: 'Efectivo', activa },
          { id: '02', claveSat: '02', descripcion: 'Cheque nominativo', activa: false },
        ]),
      ),
    );
    const queryClient = createTestQueryClient();
    render(
      <RegistrarCobroCard comprobanteId="venta-p6" folio="DEMO-P6" total={100} moneda="MXN" />,
      { wrapper: createQueryWrapper(queryClient) },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Registrar cobro' }));
    const cobrar = screen.getByRole('button', { name: 'Cobrar' });
    await waitFor(() => expect(cobrar).toBeEnabled());
    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Forma de pago 1' }), {
      key: 'ArrowDown',
    });
    expect(await screen.findByRole('option', { name: 'Efectivo' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Cheque nominativo' })).toBeNull();
    fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' });
    fireEvent.click(cobrar);
    expect(registrar).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({
          formasPago: [{ formaPago: '01', importe: 100 }],
        }),
      }),
      expect.anything(),
    );
    registrar.mockClear();
    activa = false;
    await queryClient.invalidateQueries({ queryKey: catalogosKeys.formasPago() });
    await waitFor(() => expect(cobrar).toBeDisabled());
    expect(cobrar).toHaveAttribute('title', 'Selecciona formas de pago SAT activas.');
    fireEvent.click(cobrar);
    expect(registrar).not.toHaveBeenCalled();
  });
});
