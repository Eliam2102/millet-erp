import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { LiquidacionRutaPage } from './LiquidacionRutaPage';

const state = vi.hoisted(() => ({ lista: vi.fn(), detalle: vi.fn(), liquidar: vi.fn(), cierre: vi.fn() }));

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  Breadcrumbs: () => null,
}));
vi.mock('../api/liquidacion', () => ({
  useLiquidaciones: state.lista,
  useLiquidacion: state.detalle,
  useLiquidarRuta: () => ({ mutateAsync: state.liquidar, isPending: false }),
  useCierreDiaContraEntrega: () => ({ mutateAsync: state.cierre, isPending: false }),
}));

const resumen = (estado: string) => ({
  salidaId: 'h1', folio: 'HS-20261105-A1B2C', sucursalId: 's1', destino: 'Obra Mérida norte', emitidaAt: '2026-11-05T15:00:00Z', pedidos: 1, contraEntrega: 1, estado, diferencia: 0,
});
const dto = (estado: string, voucher = 0) => ({
  salidaId: 'h1', folio: 'HS-20261105-A1B2C', destino: 'Obra Mérida norte', estado, liquidadaAt: estado === 'Pendiente' ? null : '2026-11-05T22:00:00Z',
  albaranes: [{ albaranId: 'a1', albaranFolio: 'HS-20261105-A1B2C/1', numeroPedido: '260418', cliente: 'Constructora Peninsular', contraEntrega: true, esperado: 8500, firmado: voucher > 0,
    efectivo: 0, voucher, voucherReferencia: voucher > 0 ? 'TPV-004821' : null, diferencia: 8500 - voucher, conciliado: voucher === 8500, avisado: false }],
  imprevistos: [], totalEsperado: 8500, totalCobrado: voucher, diferencia: 8500 - voucher,
});

beforeEach(() => {
  vi.clearAllMocks();
  state.liquidar.mockResolvedValue({ data: dto('Cerrada', 8500) });
  state.cierre.mockResolvedValue({ data: { avisos: 1 } });
  state.lista.mockReturnValue({ data: { items: [resumen('Pendiente')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
  state.detalle.mockReturnValue({ data: dto('Pendiente') });
  useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar] });
});

describe('LiquidacionRutaPage', () => {
  it('el voucher que no coincide con el pedido muestra la diferencia y la liquidación sigue abierta', async () => {
    render(<LiquidacionRutaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Liquidar' }));
    fireEvent.click(screen.getByLabelText('Albarán HS-20261105-A1B2C/1 firmado'));
    fireEvent.change(screen.getByLabelText('Voucher HS-20261105-A1B2C/1'), { target: { value: '8000' } });
    expect(screen.getByText(/Falta/)).toBeInTheDocument();
    // un voucher exige su folio, el tipo de tarjeta y los últimos 4 dígitos (M-177: la caja lo registra con ellos)
    const liquidar = screen.getByRole('button', { name: 'Liquidar ruta' });
    expect(liquidar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Folio del voucher HS-20261105-A1B2C/1'), { target: { value: 'TPV-004821' } });
    expect(liquidar).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Débito' }));
    expect(screen.getByRole('button', { name: 'Débito' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.change(screen.getByLabelText('Últimos 4 de la tarjeta HS-20261105-A1B2C/1'), { target: { value: '48a21' } });
    expect(screen.getByLabelText('Últimos 4 de la tarjeta HS-20261105-A1B2C/1')).toHaveValue('4821');
    fireEvent.click(liquidar);
    await waitFor(() => expect(state.liquidar).toHaveBeenCalled());
    expect(state.liquidar.mock.calls[0]![0]).toMatchObject({
      salidaId: 'h1',
      body: {
        albaranes: [{ albaranId: 'a1', firmado: true, efectivo: 0, voucher: 8000, voucherReferencia: 'TPV-004821', tipoTarjeta: 'Debito', tarjetaUltimos4: '4821' }],
        imprevistos: [],
      },
    });
  });

  it('una liquidación con diferencia avisa que no se cierra y deja corregir', () => {
    state.lista.mockReturnValue({ data: { items: [resumen('ConDiferencia')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    state.detalle.mockReturnValue({ data: dto('ConDiferencia', 8000) });
    render(<LiquidacionRutaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Liquidar' }));
    expect(screen.getByText(/no se cierra/)).toBeInTheDocument();
    expect(screen.getByLabelText('Voucher HS-20261105-A1B2C/1')).toBeEnabled();
  });

  it('una liquidación cerrada solo se consulta', () => {
    state.lista.mockReturnValue({ data: { items: [resumen('Cerrada')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    state.detalle.mockReturnValue({ data: dto('Cerrada', 8500) });
    render(<LiquidacionRutaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver liquidación' }));
    expect(screen.getByLabelText('Voucher HS-20261105-A1B2C/1')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Liquidar ruta' })).toBeNull();
  });

  it('M-177: el efectivo no pide datos de tarjeta y la línea muestra la factura y el cobro en caja', async () => {
    state.detalle.mockReturnValue({ data: { ...dto('Pendiente'), albaranes: [{ ...dto('Pendiente').albaranes[0], facturaFolio: 'FA-000101', cobradoEnCaja: false }] } });
    render(<LiquidacionRutaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Liquidar' }));
    expect(screen.getByText('FA-000101')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Albarán HS-20261105-A1B2C/1 firmado'));
    fireEvent.change(screen.getByLabelText('Efectivo HS-20261105-A1B2C/1'), { target: { value: '8500' } });
    expect(screen.getByRole('button', { name: 'Crédito' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Liquidar ruta' }));
    await waitFor(() => expect(state.liquidar).toHaveBeenCalled());
    expect(state.liquidar.mock.calls[0]![0].body.albaranes[0]).toMatchObject({ efectivo: 8500, voucher: 0, tipoTarjeta: null, tarjetaUltimos4: null });
  });

  it('M-177: una liquidación cerrada muestra que el cobro ya entró a caja', () => {
    state.lista.mockReturnValue({ data: { items: [resumen('Cerrada')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    state.detalle.mockReturnValue({ data: { ...dto('Cerrada', 8500), albaranes: [{ ...dto('Cerrada', 8500).albaranes[0], cobradoEnCaja: true }] } });
    render(<LiquidacionRutaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver liquidación' }));
    expect(screen.getByText('Cobrado en caja')).toBeInTheDocument();
    expect(screen.getByText(/entró a tu sesión de caja/)).toBeInTheDocument();
  });

  it('el cierre del día avisa cuántas contra entregas se mandaron a CxC', async () => {
    render(<LiquidacionRutaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar el día de caja' }));
    expect(await screen.findByText(/avisó a Crédito y Cobranza de 1 contra entrega/)).toBeInTheDocument();
  });
});
