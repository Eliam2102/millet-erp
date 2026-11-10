import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaCancelaciones } from '@/features/facturacion/pages/BandejaCancelaciones';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

const toastMock = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('sonner', () => ({ toast: toastMock }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
}));

const pendiente = {
  solicitudId: 's-1',
  comprobanteId: 'f-1',
  tipoComprobante: 'FacturaVenta',
  folio: 'FV-100',
  uuid: '11111111-2222-3333-4444-555555555555',
  receptorRfc: 'AAA010101AAA',
  receptorNombre: 'Cliente demo',
  total: 1160,
  moneda: 'MXN',
  estadoComprobante: 'Timbrado',
  motivoSat: '02',
  uuidSustituto: null,
  estadoSolicitud: 'PendienteAutorizacion',
  solicitadaPor: 'Ingresos demo',
  solicitadaPorId: 'u-ingresos',
  solicitadaEn: '2026-10-31T10:00:00Z',
  decididaPor: null,
  decididaEn: null,
  motivoRechazo: null,
};

describe('<BandejaCancelaciones> — smoke (F6.5)', () => {
  it('lista las pendientes y autoriza', async () => {
    let autorizada = false;
    mswServer.use(
      http.get('*/api/v1/facturacion/comprobantes/cancelaciones', () =>
        HttpResponse.json(autorizada ? [] : [pendiente]),
      ),
      http.post('*/api/v1/facturacion/comprobantes/f-1/cancelar/autorizar', () => {
        autorizada = true;
        return HttpResponse.json({
          solicitudId: 's-1',
          comprobanteId: 'f-1',
          estadoComprobante: 'CancelacionPendiente',
          estadoSolicitud: 'EnProceso',
          estatusSat: 'EnProceso',
        });
      }),
    );

    render(<BandejaCancelaciones />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('FV-100')).toBeInTheDocument());
    expect(screen.getByText('Ingresos demo')).toBeInTheDocument();
    expect(screen.getByText(/02 — Comprobante con errores sin relación/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    await waitFor(() => expect(screen.getByText('No hay solicitudes en esta vista.')).toBeInTheDocument());
    expect(autorizada).toBe(true);
  });

  it('M18: si el SAT rechaza la cancelación, avisa con error y el motivo del SAT', async () => {
    toastMock.success.mockClear();
    toastMock.error.mockClear();
    mswServer.use(
      http.get('*/api/v1/facturacion/comprobantes/cancelaciones', () => HttpResponse.json([pendiente])),
      http.post('*/api/v1/facturacion/comprobantes/f-1/cancelar/autorizar', () =>
        HttpResponse.json({
          solicitudId: 's-1',
          comprobanteId: 'f-1',
          estadoComprobante: 'Timbrado',
          estadoSolicitud: 'Rechazada',
          estatusSat: 'Rechazada',
          mensajeError: 'SAT 205: el SAT aún no refleja este CFDI',
        }),
      ),
    );

    render(<BandejaCancelaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FV-100')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));

    await waitFor(() => expect(toastMock.error).toHaveBeenCalled());
    expect(toastMock.error.mock.calls[0][0]).toMatch(/El SAT rechazó la cancelación de FV-100.*Motivo: SAT 205/);
    expect(toastMock.success).not.toHaveBeenCalled();
  });

  it('no autoriza sin motivo y envía el motivo al rechazar', async () => {
    let motivo: string | null = null;
    mswServer.use(
      http.get('*/api/v1/facturacion/comprobantes/cancelaciones', () =>
        HttpResponse.json(motivo ? [] : [pendiente]),
      ),
      http.post('*/api/v1/facturacion/comprobantes/f-1/cancelar/rechazar', async ({ request }) => {
        motivo = ((await request.json()) as { motivo: string }).motivo;
        return HttpResponse.json({
          solicitudId: 's-1',
          comprobanteId: 'f-1',
          estadoComprobante: 'Timbrado',
          estadoSolicitud: 'RechazadaPorAutorizador',
          estatusSat: null,
        });
      }),
    );

    render(<BandejaCancelaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FV-100')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'No autorizar' }));
    fireEvent.click(screen.getByRole('button', { name: 'No autorizar' }));
    expect(motivo).toBeNull();

    fireEvent.change(screen.getByLabelText('Motivo para no autorizar FV-100'), {
      target: { value: 'Se corrige con nota de crédito' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'No autorizar' }));
    await waitFor(() => expect(motivo).toBe('Se corrige con nota de crédito'));
  });

  it('O2: a quien solicitó no le ofrece autorizar su propia solicitud', async () => {
    useAuthStore.setState({ user: { id: 'u-ingresos', email: 'i@demo.local', nombre: 'Ingresos demo' } } as never);
    mswServer.use(
      http.get('*/api/v1/facturacion/comprobantes/cancelaciones', () => HttpResponse.json([pendiente])),
    );

    render(<BandejaCancelaciones />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('La solicitaste tú; la autoriza otra persona.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'No autorizar' })).toBeNull();
    useAuthStore.setState({ user: null } as never);
  });

  it('B3: «Todos» pide todas las solicitudes y muestra la rechazada por el SAT con su estado', async () => {
    const urls: string[] = [];
    const rechazadaSat = {
      ...pendiente,
      solicitudId: 's-2',
      comprobanteId: 'f-2',
      folio: 'FV-200',
      estadoSolicitud: 'Rechazada',
      decididaPor: 'Contador demo',
      decididaEn: '2026-11-01T10:00:00Z',
    };
    mswServer.use(
      http.get('*/api/v1/facturacion/comprobantes/cancelaciones', ({ request }) => {
        urls.push(request.url);
        const todos = new URL(request.url).searchParams.get('todos') === 'true';
        return HttpResponse.json(todos ? [pendiente, rechazadaSat] : [pendiente]);
      }),
    );

    render(<BandejaCancelaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FV-100')).toBeInTheDocument());
    expect(screen.getByRole('tab', { name: 'Rechazada por el SAT' })).toBeInTheDocument();

    fireEvent.mouseDown(screen.getByRole('tab', { name: /Todos/ }), { button: 0 });

    await waitFor(() => expect(screen.getByText('FV-200')).toBeInTheDocument());
    expect(urls.some((u) => new URL(u).searchParams.get('todos') === 'true')).toBe(true);
    expect(urls.some((u) => u.includes('todos=true') && u.includes('estado='))).toBe(false);
    expect(screen.getByText('Rechazada por el SAT', { selector: 'p' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).toBeNull();
  });
});
