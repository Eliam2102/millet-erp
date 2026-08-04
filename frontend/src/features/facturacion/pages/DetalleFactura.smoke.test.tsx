import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleFactura } from '@/features/facturacion/pages/DetalleFactura';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'f-1' }),
  useSearch: () => ({}),
}));

const detalle = {
  id: 'f-1',
  tipo: 'Ingreso',
  folio: 'A-1',
  estado: 'Timbrado',
  uuid: '11111111-2222-3333-4444-555555555555',
  receptorRfc: 'XAXX010101000',
  receptorNombre: 'Público en general',
  moneda: 'MXN',
  subtotal: 100,
  descuento: 0,
  impuestosTrasladados: 16,
  retenciones: 0,
  total: 116,
  fechaTimbrado: '2026-05-30T10:00:00Z',
  version: 2,
  lineas: [
    {
      posicion: 1,
      claveProdServSat: '01010101',
      descripcion: 'Vidrio templado 6mm',
      claveUnidadSat: 'H87',
      cantidad: 1,
      valorUnitario: 100,
      descuento: 0,
      importe: 100,
    },
  ],
  relaciones: [
    {
      tipoRelacion: '07',
      uuidRelacionado: 'AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE',
      folio: 'FANT-9',
      tipoComprobante: 'I',
      total: 50,
      fechaTimbrado: '2026-05-29T10:00:00Z',
    },
  ],
  // [Decisión 13-K]
  totalAcreditado: 0,
  totalPorCobrar: 116,
  notasCreditoAplicadas: [],
};

describe('<DetalleFactura> — smoke', () => {
  it('renderiza folio, conceptos, cadena de relaciones y bitácora de envío', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json(detalle),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () =>
        HttpResponse.json([
          {
            id: 'e-1',
            destinatario: 'cliente@demo.com',
            estado: 'Enviado',
            intentos: 1,
            enviadoAt: '2026-05-30T12:00:00Z',
            ultimoError: null,
          },
        ]),
      ),
      http.get('*/api/v1/facturacion/comprobantes/f-1/cancelar', () =>
        HttpResponse.json({
          comprobanteId: 'f-1',
          estadoComprobante: 'Timbrado',
          solicitudId: null,
          estadoSolicitud: null,
          motivoSat: null,
          estatusSat: null,
          mensajeError: null,
          solicitadaEn: null,
          resueltaEn: null,
        }),
      ),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());
    expect(screen.getByText('Vidrio templado 6mm')).toBeInTheDocument();
    expect(screen.getByText(/Conceptos \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText(/Cadena de relaciones CFDI \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText(/Aplicación de anticipo/i)).toBeInTheDocument();
    expect(
      await screen.findByText('cliente@demo.com'),
    ).toBeInTheDocument();
    // Acciones de descarga presentes.
    expect(screen.getByRole('button', { name: /^XML$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Imprimir/i })).toBeInTheDocument();
  });

  it('muestra NC aplicadas y el monto por cobrar ([Decisión 13-K])', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({
          ...detalle,
          relaciones: [],
          totalAcreditado: 50,
          totalPorCobrar: 66,
          notasCreditoAplicadas: [
            {
              id: 'nc-1',
              folio: 'NC-3',
              motivo: 'Amortizacion',
              total: 50,
              uuid: 'BBBBBBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF',
              fechaTimbrado: '2026-05-30T11:00:00Z',
            },
          ],
        }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());
    expect(screen.getByText(/Notas de crédito aplicadas \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText('NC-3')).toBeInTheDocument();
    expect(screen.getByText(/Aplicación de anticipo/i)).toBeInTheDocument();
    expect(screen.getByText('Por cobrar')).toBeInTheDocument();
    expect(screen.getByText('66.00 MXN')).toBeInTheDocument();
  });

  it('estado error cuando la factura no existe', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'No encontrada', status: 404 },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudo cargar la factura/i),
      ).toBeInTheDocument(),
    );
  });
});
