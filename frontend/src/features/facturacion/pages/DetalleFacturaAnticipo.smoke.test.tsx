import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleFacturaAnticipo } from '@/features/facturacion/pages/DetalleFacturaAnticipo';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'fa-1' }),
  useSearch: () => ({}),
}));

const detalle = {
  id: 'fa-1',
  folio: 'FANT-2026-000003',
  estado: 'Timbrado',
  uuid: '11111111-2222-3333-4444-555555555555',
  receptorNombre: 'Cliente Maquila',
  receptorRfc: 'AAA010101AAA',
  tipoAnticipo: 'ClientesMxp',
  anticipoId: 'a-1',
  pedidoFacturableId: null,
  descripcion: 'Anticipo de clientes',
  subtotal: 1000,
  impuestosTrasladados: 160,
  total: 1160,
  moneda: 'MXN',
  fechaTimbrado: '2026-07-12T10:00:00Z',
  version: 3,
  timbradoErrorCodigo: null,
  timbradoErrorMensaje: null,
  relaciones: [],
  anticipo: {
    anticipoId: 'a-1',
    clienteId: 'c-1',
    estado: 'Abierto',
    montoCobrado: 1160,
    montoAmortizado: 500,
    saldo: 660,
    saldoDisponible: 660,
    pedidoOrigenRef: 'AW-7',
    obraId: null,
    obraNombre: null,
    vinculaciones: [
      {
        facturaVentaId: 'f-1',
        facturaFolio: 'VEN-000003',
        facturaUuid: '22222222-2222-3333-4444-555555555555',
        facturaEstado: 'Timbrado',
        importe: 500,
        creadoEn: '2026-07-12T11:00:00Z',
        ncAmortizacionId: 'nc-1',
        ncFolio: 'NC-000001',
        ncUuid: '33333333-2222-3333-4444-555555555555',
        ncEstado: 'Timbrado',
      },
    ],
  },
};

describe('<DetalleFacturaAnticipo> — smoke (ANT-PR2)', () => {
  it('renderiza folio, KPIs de saldo y la vinculación con factura final + NC', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/facturas/fa-1', () =>
        HttpResponse.json(detalle),
      ),
      http.get('*/api/v1/facturacion/comprobantes/fa-1/cancelar', () =>
        HttpResponse.json({ estadoSolicitud: null }),
      ),
    );
    render(<DetalleFacturaAnticipo />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('FANT-2026-000003')).toBeInTheDocument(),
    );
    // KPIs del saldo (13-A/13-H)
    expect(screen.getByText('Cobrado')).toBeInTheDocument();
    expect(screen.getByText('Disponible')).toBeInTheDocument();
    // Vinculación: factura final con link + NC de amortización
    expect(screen.getByText('VEN-000003')).toBeInTheDocument();
    expect(screen.getByText('NC-000001')).toBeInTheDocument();
    // Acciones de descarga
    expect(screen.getByRole('button', { name: /XML/i })).toBeInTheDocument();
  });

  it('muestra el banner de timbrado fallido con el error del PAC (#530)', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/facturas/fa-1', () =>
        HttpResponse.json({
          ...detalle,
          estado: 'TimbradoFallido',
          uuid: null,
          fechaTimbrado: null,
          timbradoErrorCodigo: 'CFDI40100',
          timbradoErrorMensaje: 'Rechazo del PAC',
          anticipo: { ...detalle.anticipo, vinculaciones: [] },
        }),
      ),
      http.get('*/api/v1/facturacion/comprobantes/fa-1/cancelar', () =>
        HttpResponse.json({ estadoSolicitud: null }),
      ),
    );
    render(<DetalleFacturaAnticipo />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText(/CFDI40100/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Rechazo del PAC/)).toBeInTheDocument();
  });

  it('estado error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/facturas/fa-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
      http.get('*/api/v1/facturacion/comprobantes/fa-1/cancelar', () =>
        HttpResponse.json({ estadoSolicitud: null }),
      ),
    );
    render(<DetalleFacturaAnticipo />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudo cargar la factura de anticipo/i),
      ).toBeInTheDocument(),
    );
  });
});
