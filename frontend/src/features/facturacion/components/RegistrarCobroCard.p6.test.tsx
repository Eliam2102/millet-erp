import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import { RegistrarCobroCard } from './RegistrarCobroCard';

const registrar = vi.hoisted(() => vi.fn());
// F6.6: folios de transferencia vigentes que «emitió» Ingresos para el cliente.
const folios = vi.hoisted(() => ({
  data: [] as { id: string; folio: string; importe: number; pedido: string | null }[],
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/features/facturacion/api/useCajaSesiones', () => ({
  useSesionActual: () => ({ data: { sesion: { estado: 'Abierta' }, diaAnteriorPendiente: false } }),
  useRegistrarCobro: () => ({ mutate: registrar, isPending: false }),
  useFoliosTransferenciaCaja: () => ({ data: folios.data, isLoading: false, isError: false }),
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

describe('86e3tfdvt · número de autorización del voucher', () => {
  it('con tarjeta exige la autorización y rechaza el número completo de la tarjeta', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([
          { id: '01', claveSat: '01', descripcion: 'Efectivo', activa: true },
          { id: '04', claveSat: '04', descripcion: 'Tarjeta de crédito', activa: true },
        ]),
      ),
    );
    registrar.mockClear();
    render(
      <RegistrarCobroCard comprobanteId="venta-tdc" folio="DEMO-TDC" total={100} moneda="MXN" />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Registrar cobro' }));
    const cobrar = screen.getByRole('button', { name: 'Cobrar' });
    await waitFor(() => expect(cobrar).toBeEnabled());
    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Forma de pago 1' }), { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: 'Tarjeta de crédito' }));

    const autorizacion = await screen.findByLabelText('Número de autorización del voucher');
    expect(autorizacion).toHaveAttribute('aria-required', 'true');
    expect(cobrar).toBeDisabled();

    fireEvent.change(autorizacion, { target: { value: '4111 1111 1111' } });
    expect(autorizacion).toHaveAttribute('aria-invalid', 'true');
    expect(cobrar).toBeDisabled();
    expect(cobrar.getAttribute('title')).toMatch(/nunca el número de la tarjeta/);

    // Sin tope corto: los 16 dígitos se conservan completos y se marcan, no se recortan a 12.
    expect(Number(autorizacion.getAttribute('maxLength'))).toBeGreaterThanOrEqual(16);
    fireEvent.change(autorizacion, { target: { value: '4111111111111111' } });
    expect(autorizacion).toHaveValue('4111111111111111');
    expect(autorizacion).toHaveAttribute('aria-invalid', 'true');
    expect(cobrar).toBeDisabled();

    fireEvent.change(autorizacion, { target: { value: '123456' } });
    // 86e3tfdvt: sin los últimos 4 dígitos de la tarjeta sigue bloqueado.
    expect(cobrar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Últimos 4 dígitos de la tarjeta'), {
      target: { value: '1234' },
    });
    await waitFor(() => expect(cobrar).toBeEnabled());
    fireEvent.click(cobrar);
    expect(registrar).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({
          formasPago: [
            expect.objectContaining({
              formaPago: '04',
              importe: 100,
              referencia: '123456',
              tarjetaUltimos4: '1234',
            }),
          ],
        }),
      }),
      expect.anything(),
    );
  });
});

describe('86e3tfdvt · últimos 4 dígitos de la tarjeta', () => {
  it('pide exactamente 4 dígitos y en pago mixto manda un voucher por tarjeta', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([
          { id: '01', claveSat: '01', descripcion: 'Efectivo', activa: true },
          { id: '03', claveSat: '03', descripcion: 'Transferencia', activa: true },
          { id: '04', claveSat: '04', descripcion: 'Tarjeta de crédito', activa: true },
          { id: '28', claveSat: '28', descripcion: 'Tarjeta de débito', activa: true },
        ]),
      ),
    );
    registrar.mockClear();
    render(
      <RegistrarCobroCard comprobanteId="venta-mixta" folio="DEMO-MIX" total={100} moneda="MXN" />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Registrar cobro' }));
    const cobrar = screen.getByRole('button', { name: 'Cobrar' });
    await waitFor(() => expect(cobrar).toBeEnabled());

    // Efectivo no pide los últimos 4 dígitos.
    expect(screen.queryByLabelText('Últimos 4 dígitos de la tarjeta')).not.toBeInTheDocument();

    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Forma de pago 1' }), { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: 'Tarjeta de crédito' }));
    fireEvent.change(screen.getByLabelText('Importe 1'), { target: { value: '60' } });
    fireEvent.change(screen.getByLabelText('Número de autorización del voucher'), {
      target: { value: 'AUT-01' },
    });
    const ultimos1 = screen.getByLabelText('Últimos 4 dígitos de la tarjeta');
    expect(ultimos1).toHaveAttribute('aria-required', 'true');
    expect(ultimos1).toHaveAttribute('maxLength', '4');
    // Solo dígitos: las letras se descartan al teclear.
    fireEvent.change(ultimos1, { target: { value: '12a' } });
    expect(ultimos1).toHaveValue('12');
    expect(ultimos1).toHaveAttribute('aria-invalid', 'true');
    expect(cobrar).toBeDisabled();
    expect(cobrar.getAttribute('title')).toMatch(/últimos 4 dígitos/);
    fireEvent.change(ultimos1, { target: { value: '1234' } });
    expect(ultimos1).toHaveAttribute('aria-invalid', 'false');

    fireEvent.click(screen.getByRole('button', { name: /Otra forma de pago/ }));
    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Forma de pago 2' }), { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: 'Tarjeta de débito' }));
    fireEvent.change(screen.getAllByLabelText('Número de autorización del voucher')[1], {
      target: { value: 'AUT-02' },
    });
    expect(cobrar).toBeDisabled();
    fireEvent.change(screen.getAllByLabelText('Últimos 4 dígitos de la tarjeta')[1], {
      target: { value: '5678' },
    });
    await waitFor(() => expect(cobrar).toBeEnabled());
    fireEvent.click(cobrar);
    expect(registrar).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({
          formasPago: [
            expect.objectContaining({ formaPago: '04', importe: 60, referencia: 'AUT-01', tarjetaUltimos4: '1234' }),
            expect.objectContaining({ formaPago: '28', importe: 40, referencia: 'AUT-02', tarjetaUltimos4: '5678' }),
          ],
        }),
      }),
      expect.anything(),
    );
  });
});

describe('F6.6 · folio de transferencia de Ingresos', () => {
  it('la transferencia exige elegir un folio vigente del cliente y lo envía con su importe', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([
          { id: '01', claveSat: '01', descripcion: 'Efectivo', activa: true },
          { id: '03', claveSat: '03', descripcion: 'Transferencia', activa: true },
        ]),
      ),
    );
    folios.data = [{ id: 'f1', folio: 'TR-000001', importe: 8000, pedido: 'PED-100' }];
    registrar.mockClear();
    render(
      <RegistrarCobroCard comprobanteId="venta-tr" folio="DEMO-TR" total={8000} moneda="MXN" />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Registrar cobro' }));
    const cobrar = screen.getByRole('button', { name: 'Cobrar' });
    await waitFor(() => expect(cobrar).toBeEnabled());

    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Forma de pago 1' }), { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: 'Transferencia' }));

    // Sin folio no se puede aplicar (CA6.8).
    const folio = await screen.findByRole('combobox', { name: 'Folio de transferencia 1' });
    expect(folio).toHaveAttribute('aria-required', 'true');
    expect(cobrar).toBeDisabled();
    expect(cobrar.getAttribute('title')).toMatch(/folio de transferencia que emitió Ingresos/);

    fireEvent.keyDown(folio, { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: /TR-000001 · 8000.00 · PED-100/ }));
    await waitFor(() => expect(cobrar).toBeEnabled());
    fireEvent.click(cobrar);
    expect(registrar).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({
          formasPago: [
            expect.objectContaining({ formaPago: '03', importe: 8000, folioTransferencia: 'TR-000001' }),
          ],
        }),
      }),
      expect.anything(),
    );
  });

  it('sin folios vigentes avisa que Ingresos debe emitirlo y no deja cobrar', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([
          { id: '01', claveSat: '01', descripcion: 'Efectivo', activa: true },
          { id: '03', claveSat: '03', descripcion: 'Transferencia', activa: true },
        ]),
      ),
    );
    folios.data = [];
    render(
      <RegistrarCobroCard comprobanteId="venta-sin" folio="DEMO-SIN" total={500} moneda="MXN" />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Registrar cobro' }));
    const cobrar = screen.getByRole('button', { name: 'Cobrar' });
    await waitFor(() => expect(cobrar).toBeEnabled());
    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Forma de pago 1' }), { key: 'ArrowDown' });
    fireEvent.click(await screen.findByRole('option', { name: 'Transferencia' }));

    expect(await screen.findByText(/pide a Ingresos que confirme el abono y emita el folio/)).toBeInTheDocument();
    expect(cobrar).toBeDisabled();
  });
});
