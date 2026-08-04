import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleRepp } from '@/features/facturacion/pages/DetalleRepp';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'r-1' }),
  useSearch: () => ({}),
}));

describe('<DetalleRepp> — smoke', () => {
  it('renderiza el REPP y sus facturas cubiertas', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/repp/r-1', () =>
        HttpResponse.json({
          id: 'r-1',
          folio: 'REPP-1',
          estado: 'Timbrado',
          uuid: 'U-1',
          receptorNombre: 'Cliente Demo SA',
          monedaPago: 'MXN',
          importeTotalPago: 1000,
          fechaPago: '2026-05-30T12:00:00Z',
          facturasCubiertas: [
            {
              facturaVentaId: 'f-1',
              folio: 'A-9',
              facturaUuid: 'FU-9',
              numParcialidad: 1,
              importePagado: 1000,
              saldoInsoluto: 0,
              gananciaPerdidaCambiaria: 0,
            },
          ],
        }),
      ),
    );
    render(<DetalleRepp />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('REPP-1')).toBeInTheDocument());
    expect(screen.getByText('A-9')).toBeInTheDocument();
    expect(screen.getByText(/Facturas cubiertas \(1\)/i)).toBeInTheDocument();
  });

  it('error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/repp/r-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'No encontrado', status: 404 },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<DetalleRepp />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudo cargar el complemento de pago/i),
      ).toBeInTheDocument(),
    );
  });
});
