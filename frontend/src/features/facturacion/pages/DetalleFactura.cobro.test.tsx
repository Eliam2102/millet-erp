import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import type { ComprobanteDetalleResponse, CobroFacturaDetalle } from '../api/types';
import { useCancelarCobro } from '../api/useCajaSesiones';
import { DetalleFactura } from './DetalleFactura';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useParams: () => ({ id: 'f-fac05' }),
  useSearch: () => ({}),
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('../components/TrazabilidadFacturacion', () => ({ TrazabilidadFacturacion: () => null }));
vi.mock('../components/IntentosTimbradoPanel', () => ({ IntentosTimbradoPanel: () => null }));

const cobro: CobroFacturaDetalle = {
  id: 'c-fac05', fechaCobro: '2026-10-09T12:00:00Z', usuarioCobradorId: 'u-fac05', total: 116,
  formasPago: [
    { formaPago: '01', importe: 60, referencia: null },
    { formaPago: '04', importe: 56, referencia: 'AUT-FICTICIA' },
  ],
  sesion: { id: 's-fac05', cajaId: 'caja-fac05', cajaNombre: 'Caja ficticia FAC-05', diaOperacion: '2026-10-09', estado: 'Abierta' },
};

const factura: ComprobanteDetalleResponse = {
  id: 'f-fac05', tipo: 'Ingreso', folio: 'FIX-FAC-05', estado: 'Timbrado', uuid: null,
  receptorRfc: 'AAA010101AAA', receptorNombre: 'Cliente ficticio FAC-05', moneda: 'MXN',
  subtotal: 100, descuento: 0, impuestosTrasladados: 16, retenciones: 0, total: 116,
  fechaTimbrado: '2026-10-09T12:00:00Z', version: 1, lineas: [], relaciones: [],
  timbradoErrorCodigo: null, timbradoErrorMensaje: null, folioPac: null,
  totalAcreditado: 0, totalPorCobrar: 116, notasCreditoAplicadas: [],
  metodoPago: 'PUE', cobroMostrador: null, pagadoPorRep: 0,
};

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/facturacion/facturas/f-fac05/envios', () => HttpResponse.json([])),
    http.get('*/api/v1/facturacion/comprobantes/f-fac05/cancelar', () => HttpResponse.json({ estadoSolicitud: null })),
    http.get('*/api/v1/facturacion/cajas/sesion-actual', () => HttpResponse.json({ sesion: { estado: 'Abierta' }, diaAnteriorPendiente: false })),
    http.get('*/api/v1/catalogos/formas-pago', () => HttpResponse.json([{ id: '01', claveSat: '01', descripcion: 'Efectivo', activa: true }])),
  );
});

describe('FAC-05 · detalle de cobro', () => {
  it('PUE sin cobro ofrece Registrar cobro', async () => {
    mswServer.use(http.get('*/api/v1/facturacion/facturas/f-fac05', () => HttpResponse.json(factura)));
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('button', { name: 'Registrar cobro' }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByText('Por cobrar')).toBeInTheDocument();
  });

  it('PUE cobrada persiste al cargar el detalle y muestra fecha, caja, importe y formas', async () => {
    mswServer.use(http.get('*/api/v1/facturacion/facturas/f-fac05', () => HttpResponse.json({ ...factura, cobroMostrador: cobro, totalPorCobrar: 0 })));
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Cobrada')).toBeInTheDocument();
    expect(screen.getByText('Fecha de cobro')).toBeInTheDocument();
    const panel = screen.getByRole('region', { name: 'Cobro de la factura' });
    expect(within(panel).getByText(new Date(cobro.fechaCobro).toLocaleString('es-MX'))).toBeInTheDocument();
    expect(screen.getByText('Caja ficticia FAC-05')).toBeInTheDocument();
    expect(screen.getByText('$116.00 MXN')).toBeInTheDocument();
    expect(screen.getByText('Forma SAT 04 · AUT-FICTICIA')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar cobro' })).not.toBeInTheDocument();
  });

  it.each([
    [0, 116, 'Por cobrar'],
    [40, 76, 'Parcial (PPD)'],
    [116, 0, 'Cobrada'],
  ])('PPD pagado %s saldo %s muestra REP sin cobro de caja', async (pagadoPorRep, totalPorCobrar, estado) => {
    mswServer.use(http.get('*/api/v1/facturacion/facturas/f-fac05', () => HttpResponse.json({ ...factura, metodoPago: 'PPD', pagadoPorRep, totalPorCobrar })));
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Se cobra con complemento de pago (REP)')).toBeInTheDocument();
    expect(screen.getByText(estado)).toBeInTheDocument();
    expect(screen.getByText('Pagado por REP')).toBeInTheDocument();
    expect(screen.getByText('Saldo')).toBeInTheDocument();
    expect(screen.getByText(`$${pagadoPorRep.toFixed(2)} MXN`)).toBeInTheDocument();
    expect(screen.getByText(`$${totalPorCobrar.toFixed(2)} MXN`)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver bandeja de REP' })).toHaveAttribute('href', '/facturacion/repp');
    expect(screen.queryByRole('button', { name: 'Registrar cobro' })).not.toBeInTheDocument();
  });

  it('registrar invalida el detalle y cambia a Cobrada sin recargar; cancelar vuelve a permitir cobrar', async () => {
    let registrada = false;
    let lecturas = 0;
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-fac05', () => {
        lecturas++;
        return HttpResponse.json({ ...factura, cobroMostrador: registrada ? cobro : null, totalPorCobrar: registrada ? 0 : 116 });
      }),
      http.post('*/api/v1/facturacion/cobros', () => {
        registrada = true;
        return HttpResponse.json({ id: cobro.id, comprobanteId: factura.id, cajaSesionId: cobro.sesion.id, total: 116, estado: 'Registrado' }, { status: 201 });
      }),
      http.post('*/api/v1/facturacion/cobros/c-fac05/cancelar', () => {
        registrada = false;
        return HttpResponse.json({ id: cobro.id, comprobanteId: factura.id, cajaSesionId: cobro.sesion.id, total: 116, estado: 'Cancelado' });
      }),
    );
    const wrapper = createQueryWrapper(createTestQueryClient());
    render(<DetalleFactura />, { wrapper });
    fireEvent.click(await screen.findByRole('button', { name: 'Registrar cobro' }, { timeout: 5000 }));
    const cobrar = screen.getByRole('button', { name: 'Cobrar' });
    await waitFor(() => expect(cobrar).toBeEnabled());
    fireEvent.click(cobrar);
    expect(await screen.findByText('Cobrada')).toBeInTheDocument();
    expect(lecturas).toBeGreaterThanOrEqual(2);
    expect(screen.queryByRole('button', { name: 'Registrar cobro' })).not.toBeInTheDocument();

    const cancelar = renderHook(() => useCancelarCobro(), { wrapper });
    await cancelar.result.current.mutateAsync({ cobroId: cobro.id, motivo: 'Prueba ficticia', idempotencyKey: 'FIX-CANCELAR' });
    expect(await screen.findByRole('button', { name: 'Registrar cobro' }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByText('Cobrada')).not.toBeInTheDocument();
    expect(lecturas).toBeGreaterThanOrEqual(3);
  });
});
