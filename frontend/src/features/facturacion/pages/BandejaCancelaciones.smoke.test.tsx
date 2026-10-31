import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaCancelaciones } from '@/features/facturacion/pages/BandejaCancelaciones';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

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
});
