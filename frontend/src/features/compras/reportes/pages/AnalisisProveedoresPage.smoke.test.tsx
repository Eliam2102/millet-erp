import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AnalisisProveedoresPage } from '@/features/compras/reportes/pages/AnalisisProveedoresPage';

let search: Record<string, unknown> = {};
vi.mock('@tanstack/react-router', () => ({
  useSearch: () => ({ tab: 'compras', agruparPor: 'proveedor', ...search }),
  useNavigate: () => vi.fn(),
  Link: ({ children, to, className }: { children: React.ReactNode; to: string; className?: string }) => (
    <a href={to} className={className}>{children}</a>
  ),
}));

const BASE = '*/api/v1/compras/reportes/analisis-proveedores';

function reporte(titulo: string, columnas: [string, string, number][], filas: Record<string, unknown>[]) {
  return {
    titulo,
    generadoEn: '2026-10-22T12:00:00Z',
    filtrosAplicados: [],
    columnas: columnas.map(([key, label, tipo]) => ({ key, label, tipo, alineacion: 1 })),
    filas,
    totales: null,
  };
}

beforeEach(() => {
  search = {};
  mswServer.use(
    http.get(`${BASE}/compras`, () =>
      HttpResponse.json({
        reporte: reporte(
          'Compras del periodo',
          [['grupo', 'Proveedor', 1], ['moneda', 'Moneda', 1], ['ordenado', 'Ordenado', 4]],
          [{ grupoId: 'p-1', grupo: 'Vidrios del Norte', moneda: 'MXN', ordenado: 1500 }],
        ),
        totalesPorMoneda: [
          { moneda: 'MXN', ordenado: 1500 },
          { moneda: 'USD', ordenado: 30 },
        ],
      }),
    ),
    http.get(`${BASE}/saldos`, () =>
      HttpResponse.json({
        reporte: reporte(
          'Saldos por orden',
          [['folio', 'Folio', 1], ['porRecibir', 'Por recibir', 4]],
          [{ ocId: 'oc-1', folio: 'OC-2026-000123', porRecibir: 400 }],
        ),
        totalesPorMoneda: [{ moneda: 'MXN', porRecibir: 400 }],
        saldoInicial: [{ moneda: 'MXN', ordenes: 3, pendienteFisico: 1200 }],
      }),
    ),
    http.get(`${BASE}/cumplimiento`, () =>
      HttpResponse.json({
        reporte: reporte(
          'Cumplimiento de proveedores',
          [['proveedor', 'Proveedor', 1], ['porcentajeATiempo', '% a tiempo', 3], ['porcentajeCompleto', '% completo', 3]],
          [{ proveedorId: 'p-1', proveedor: 'Vidrios del Norte', porcentajeATiempo: 87.5, porcentajeCompleto: null }],
        ),
        totalesPorMoneda: [],
        formulas: ['A tiempo = líneas completadas en o antes del compromiso vigente / líneas evaluables'],
      }),
    ),
    http.get('*/api/v1/catalogos/*', () => HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 })),
  );
});

const renderPage = () => render(<AnalisisProveedoresPage />, { wrapper: createQueryWrapper() });

describe('AnalisisProveedoresPage', () => {
  it('muestra las 3 pestañas y la de Compras con totales por moneda sin sumarlas', async () => {
    renderPage();
    expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Compras', 'Saldos', 'Cumplimiento']);
    expect(await screen.findByText('Vidrios del Norte')).toBeInTheDocument();
    expect(screen.getByText('Total MXN')).toBeInTheDocument();
    expect(screen.getByText('Total USD')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ver órdenes de Vidrios del Norte' })).toBeInTheDocument();
  });

  it('Saldos: tabla por OC y bloque de saldo inicial por moneda', async () => {
    search = { tab: 'saldos' };
    renderPage();
    expect(await screen.findByRole('cell', { name: 'OC-2026-000123' })).toBeInTheDocument();
    expect(screen.getByText('Saldo inicial por moneda')).toBeInTheDocument();
    expect(screen.getByText(/MXN · 3 órdenes/)).toBeInTheDocument();
  });

  it('Cumplimiento: «n/d» para porcentaje null, nunca NaN, y fórmulas visibles', async () => {
    search = { tab: 'cumplimiento' };
    const { container } = renderPage();
    expect(await screen.findByText('87.5 %')).toBeInTheDocument();
    expect(screen.getByText('n/d', { selector: 'td' })).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.getByText('A tiempo = líneas completadas en o antes del compromiso vigente / líneas evaluables'),
      ).toBeInTheDocument(),
    );
    expect(container.textContent).not.toContain('NaN');
  });

  it('periodo inválido: avisa y no consulta', async () => {
    search = { desde: '2026-10-10', hasta: '2026-10-01' };
    renderPage();
    expect(await screen.findByRole('alert')).toHaveTextContent('no puede ser anterior');
    expect(screen.queryByText('Vidrios del Norte')).not.toBeInTheDocument();
  });
});
