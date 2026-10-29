import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleFactura } from '@/features/facturacion/pages/DetalleFactura';

const descargarArchivo = vi.hoisted(() => vi.fn().mockResolvedValue(undefined));
vi.mock('@/features/facturacion/lib/descargas', () => ({ descargarArchivo }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
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
  lineas: [],
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
    {
      id: 'nc-2',
      folio: 'NC-4',
      motivo: 'Bonificacion',
      total: 10,
      uuid: null,
      fechaTimbrado: null,
    },
  ],
};

describe('FMT-4 · archivos de las NC aplicadas en el detalle de la factura', () => {
  it('descarga el PDF y el XML de la NC timbrada desde su renglón', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json(detalle)),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('NC-3')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'PDF de la nota de crédito NC-3' }));
    fireEvent.click(screen.getByRole('button', { name: 'XML de la nota de crédito NC-3' }));

    await waitFor(() =>
      expect(descargarArchivo).toHaveBeenCalledWith('/api/v1/facturacion/notas-credito/nc-1/pdf', 'NC-3.pdf'),
    );
    expect(descargarArchivo).toHaveBeenCalledWith('/api/v1/facturacion/notas-credito/nc-1/xml', 'NC-3.xml');
    // Sin UUID (no timbrada) no hay archivos que descargar.
    expect(screen.queryByRole('button', { name: 'PDF de la nota de crédito NC-4' })).not.toBeInTheDocument();
  });
});
