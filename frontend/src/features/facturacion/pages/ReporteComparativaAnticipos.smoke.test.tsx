import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteComparativaAnticipos } from '@/features/facturacion/pages/ReporteComparativaAnticipos';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => ({}),
  Link: ({ children }: { children: ReactNode }) => <a href="#">{children}</a>,
}));

describe('<ReporteComparativaAnticipos> — smoke', () => {
  it('renderiza el anticipo y su aplicación con el egreso', async () => {
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/comparativa-anticipos', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json({
          titulo: 'Comparativa de anticipos vs aplicaciones',
          generadoEn: '2026-10-19T10:00:00Z',
          filtrosAplicados: {},
          columnas: [
            { clave: 'folioAnticipo', etiqueta: 'Anticipo', tipo: 'texto' },
            { clave: 'movimiento', etiqueta: 'Movimiento', tipo: 'texto' },
            { clave: 'egresoUuid', etiqueta: 'UUID egreso', tipo: 'texto' },
            { clave: 'saldo', etiqueta: 'Saldo', tipo: 'moneda', alineacion: 'derecha' },
          ],
          filas: [
            {
              folioAnticipo: 'FANT-30',
              movimiento: 'Anticipo',
              egresoUuid: null,
              saldo: 10000,
              facturaAnticipoId: 'fa-1',
            },
            {
              folioAnticipo: 'FANT-30',
              movimiento: 'Aplicación',
              egresoUuid: 'UUID-NC-07',
              saldo: null,
              facturaAnticipoId: 'fa-1',
            },
          ],
          totales: { saldo: 10000 },
        });
      }),
    );
    render(<ReporteComparativaAnticipos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('UUID-NC-07')).toBeInTheDocument(),
    );
    expect(screen.getAllByText('FANT-30')).toHaveLength(2);
    expect(screen.getByText('Aplicación')).toBeInTheDocument();
    // Periodo por defecto: mes en curso.
    const params = new URL(urlVista).searchParams;
    expect(params.get('desde')).not.toBeNull();
    expect(params.get('hasta')).not.toBeNull();
    expect(params.get('corte')).toBeNull();
  });
});
