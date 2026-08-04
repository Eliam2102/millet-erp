import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EstadoCuentaAnticipos } from '@/features/facturacion/pages/EstadoCuentaAnticipos';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ clienteId: 'cli-1' }),
}));

describe('<EstadoCuentaAnticipos> — smoke', () => {
  it('renderiza KPIs, anticipos y sus vinculaciones (NC)', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/control/cli-1', () =>
        HttpResponse.json({
          clienteId: 'cli-1',
          generadoEn: '2026-05-30T10:00:00Z',
          totalCobrado: 1160,
          totalAmortizado: 500,
          totalSaldo: 660,
          anticipos: [
            {
              anticipoId: 'a-1',
              folio: 'FANT-1',
              tipoAnticipo: 'ClientesMxp',
              montoCobrado: 1160,
              montoAmortizado: 500,
              saldo: 660,
              estado: 'Abierto',
              pedidoOrigenRef: null,
              vinculaciones: [
                {
                  facturaVentaId: 'f-1',
                  facturaFolio: 'A-9',
                  importe: 500,
                  ncAmortizacionId: 'nc-1',
                  ncFolio: 'NC-3',
                  ncTimbrada: true,
                },
              ],
            },
          ],
        }),
      ),
    );
    render(<EstadoCuentaAnticipos />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FANT-1')).toBeInTheDocument());
    expect(screen.getByText('A-9')).toBeInTheDocument();
    expect(screen.getByText('NC-3')).toBeInTheDocument();
  });

  it('estado error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/control/cli-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<EstadoCuentaAnticipos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudo cargar el estado de cuenta/i),
      ).toBeInTheDocument(),
    );
  });
});
