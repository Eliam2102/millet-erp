import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteEstadosAnticipos } from '@/features/facturacion/pages/ReporteEstadosAnticipos';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

describe('<ReporteEstadosAnticipos> — smoke', () => {
  it('renderiza el reporte con su fila', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/estados-anticipos', () =>
        HttpResponse.json({
          titulo: 'Estados de facturas de anticipo',
          generadoEn: '2026-05-30T10:00:00Z',
          filtrosAplicados: {},
          columnas: [
            { clave: 'folio', etiqueta: 'Folio', tipo: 'texto' },
            { clave: 'saldo', etiqueta: 'Saldo', tipo: 'moneda' },
          ],
          filas: [{ folio: 'FANT-7', saldo: 320 }],
          totales: { saldo: 320 },
        }),
      ),
    );
    render(<ReporteEstadosAnticipos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('FANT-7')).toBeInTheDocument(),
    );
  });
});
