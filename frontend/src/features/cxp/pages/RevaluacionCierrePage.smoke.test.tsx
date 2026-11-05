import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RevaluacionCierrePage } from '@/features/cxp/pages/RevaluacionCierrePage';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

function sesion(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

afterEach(() => {
  useAuthStore.setState({
    status: 'idle', accessToken: null, expiresAt: null, user: null, empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null,
  });
});

const REPORTE = {
  titulo: 'Revaluación de saldos en moneda extranjera',
  generadoEn: '2026-02-01T15:00:00Z',
  filtrosAplicados: [{ label: 'Cierre', valor: '2026-01-31' }],
  columnas: [
    { key: 'proveedor_nombre', label: 'Proveedor', tipo: 1, alineacion: 1 },
    { key: 'documento', label: 'Documento', tipo: 1, alineacion: 1 },
    { key: 'diferencia', label: 'Diferencia (MXN)', tipo: 4, alineacion: 3 },
  ],
  filas: [{ proveedor_nombre: 'Vidrios del Norte SA de CV', documento: 'FA-100', diferencia: 500 }],
  totales: { perdida: 500, utilidad: 0, neto: 500 },
};

const FILA = {
  id: 'r1', anio: 2026, mes: 1, fechaCierre: '2026-01-31', fechaReversa: '2026-02-01', totalPerdida: 500, totalUtilidad: 0,
  documentos: 1, revertida: false,
};

describe('<RevaluacionCierrePage>', () => {
  beforeEach(() => sesion([
    'cuentas_por_pagar.revaluacion.consultar',
    'cuentas_por_pagar.revaluacion.ejecutar',
  ]));

  it('lista las revaluaciones corridas con su reversa pendiente y abre el reporte por documento', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/revaluaciones', () => HttpResponse.json([FILA])),
      http.get('*/api/v1/cuentas-por-pagar/revaluaciones/2026/1/reporte', () => HttpResponse.json(REPORTE)),
    );
    render(<RevaluacionCierrePage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Enero 2026')).toBeInTheDocument();
    expect(screen.getByText('01/02/2026')).toBeInTheDocument();
    expect(screen.getByText('Pendiente')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Ver reporte de Enero 2026/i }));
    expect(await screen.findByText('Vidrios del Norte SA de CV')).toBeInTheDocument();
    expect(screen.getByText('FA-100')).toBeInTheDocument();
  });

  it('al correr la revaluación manda mes y año y muestra el resultado con la fecha de la reversa', async () => {
    let cuerpo: unknown;
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/revaluaciones', () => HttpResponse.json([])),
      http.post('*/api/v1/cuentas-por-pagar/revaluaciones', async ({ request }) => {
        cuerpo = await request.json();
        return HttpResponse.json(
          { id: 'r1', anio: 2026, mes: 1, fechaCierre: '2026-01-31', fechaReversa: '2026-02-01', documentos: 1, totalPerdida: 500, totalUtilidad: 0, omitidas: [] },
          { status: 201 },
        );
      }),
      http.get('*/api/v1/cuentas-por-pagar/revaluaciones/2026/1/reporte', () => HttpResponse.json(REPORTE)),
    );
    render(<RevaluacionCierrePage />, { wrapper: createQueryWrapper() });

    fireEvent.change(screen.getByLabelText('Año'), { target: { value: '2026' } });
    fireEvent.click(screen.getByRole('button', { name: /Correr revaluación/i }));

    const resultado = await screen.findByText(
      (_, el) => el?.tagName === 'P' && /1 documento revaluado/.test(el.textContent ?? ''),
    );
    expect(resultado).toHaveTextContent(/01\/02\/2026/);
    expect(cuerpo).toMatchObject({ anio: 2026 });
  });

  it('sin permiso de ejecutar no ofrece correr la revaluación', async () => {
    sesion(['cuentas_por_pagar.revaluacion.consultar']);
    mswServer.use(http.get('*/api/v1/cuentas-por-pagar/revaluaciones', () => HttpResponse.json([])));
    render(<RevaluacionCierrePage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText(/Aún no hay revaluaciones/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Correr revaluación/i })).not.toBeInTheDocument();
  });
});
