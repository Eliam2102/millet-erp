import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { RetencionesPage } from './RetencionesPage';
import { ReporteAuxiliarPage } from './ReporteAuxiliarPage';
import { ReportePasivosObrasPage } from './ReportePasivosObrasPage';
import { CapturarFacturaSheet } from '../components/CapturarFacturaSheet';
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="/cxp">{children}</a>,
  useNavigate: () => () => {},
}));
const regla = {
  id: '0199aa00-0000-7000-8000-000000000001',
  concepto: 'HONORARIOS_PF',
  descripcion: 'Honorarios FIX',
  impuesto: '001',
  tasa: 0.1,
  activa: true,
  fuente: 'https://www.sat.gob.mx/',
  aviso: 'Supuesto SAT, valida Fiscal (D03)',
  motivoCambio: 'FIX propuesta Fiscal',
  version: 1,
};
beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600000),
    user: { id: 'FIX-P8', email: 'FIX@example.test', nombre: 'FIX P8' },
    empresas: [],
    currentEmpresaId: 'FIX-empresa',
    permisos: [
      'cuentas_por_pagar.retenciones.leer',
      'cuentas_por_pagar.retenciones.administrar',
      'cuentas_por_pagar.reportes.cartera',
      'cuentas_por_pagar.facturas.capturar',
    ],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/cuentas-por-pagar/catalogos/retenciones', () => HttpResponse.json([regla])),
  );
});
describe('P8: catálogo y reportes visibles', () => {
  it('edita tasa con motivo, versión e idempotencia y mantiene el aviso Fiscal', async () => {
    const enviado = vi.fn();
    mswServer.use(
      http.put('*/api/v1/cuentas-por-pagar/catalogos/retenciones/:id', async ({ request }) => {
        enviado(
          await request.json(),
          request.headers.get('X-Expected-Version'),
          request.headers.get('Idempotency-Key'),
        );
        return HttpResponse.json({ ...regla, tasa: 0.125, version: 2 });
      }),
    );
    render(<RetencionesPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('HONORARIOS_PF')).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('Supuesto SAT, valida Fiscal (D03)');
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }));
    fireEvent.change(screen.getByLabelText('Tasa (%)'), { target: { value: '12.5' } });
    fireEvent.change(screen.getByLabelText('Motivo del cambio'), {
      target: { value: 'FIX ajuste aprobado por Fiscal' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar retención' }));
    await waitFor(() =>
      expect(enviado).toHaveBeenCalledWith(
        expect.objectContaining({ tasa: 0.125, motivo: 'FIX ajuste aprobado por Fiscal' }),
        '1',
        expect.any(String),
      ),
    );
  });
  it('el permiso de lectura permite consultar pero oculta edición', async () => {
    useAuthStore.setState({ permisos: ['cuentas_por_pagar.retenciones.leer'] });
    render(<RetencionesPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('HONORARIOS_PF');
    expect(screen.queryByRole('button', { name: 'Nueva retención' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
  });
  it('auxiliar usa corte pasado, nombre y RFC y presenta totales separados por moneda', async () => {
    const consulta = vi.fn();
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/reportes/auxiliar-proveedores', ({ request }) => {
        consulta(new URL(request.url).searchParams.get('fechaCorte'));
        return HttpResponse.json({
          titulo: 'Auxiliar de proveedores a una fecha',
          generadoEn: '2026-10-09T00:00:00Z',
          filtrosAplicados: [],
          columnas: [
            { key: 'proveedor_nombre', label: 'Proveedor', tipo: 1, alineacion: 1 },
            { key: 'rfc', label: 'RFC', tipo: 1, alineacion: 1 },
            { key: 'moneda', label: 'Moneda', tipo: 1, alineacion: 1 },
            { key: 'total', label: 'Saldo', tipo: 4, alineacion: 3 },
          ],
          filas: [
            {
              proveedor_id: 'FIX-guid-interno',
              proveedor_nombre: 'Proveedor FIX',
              rfc: 'FIX010101ABC',
              moneda: 'MXN',
              total: 900,
            },
            {
              proveedor_id: 'FIX-guid-interno',
              proveedor_nombre: 'Proveedor FIX',
              rfc: 'FIX010101ABC',
              moneda: 'USD',
              total: 50,
            },
          ],
          totales: {
            por_moneda: [
              { moneda: 'MXN', total: 900 },
              { moneda: 'USD', total: 50 },
            ],
          },
        });
      }),
    );
    render(<ReporteAuxiliarPage />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText('Fecha de corte'), { target: { value: '2026-09-30' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ejecutar' }));
    expect(await screen.findByText('Total MXN')).toBeInTheDocument();
    expect(screen.getByText('Total USD')).toBeInTheDocument();
    expect(screen.getAllByText('FIX010101ABC')).toHaveLength(2);
    expect(screen.queryByText('FIX-guid-interno')).not.toBeInTheDocument();
    expect(consulta).toHaveBeenCalledWith('2026-09-30');
  });
  it('el filtro por obra se envía sin convertirlo en sucursal', async () => {
    const consulta = vi.fn();
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/reportes/pasivos-obras', ({ request }) => {
        const p = new URL(request.url).searchParams;
        consulta(p.get('obra'), p.get('sucursalId'));
        return HttpResponse.json({
          titulo: 'Pasivos por obra',
          generadoEn: '2026-10-09T00:00:00Z',
          filtrosAplicados: [],
          columnas: [{ key: 'obra', label: 'Obra', tipo: 1, alineacion: 1 }],
          filas: [{ obra: 'FIX-OBRA-A' }],
          totales: null,
        });
      }),
    );
    render(<ReportePasivosObrasPage />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText('Obra'), { target: { value: 'FIX-OBRA-A' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ejecutar' }));
    expect(await screen.findByText('FIX-OBRA-A')).toBeInTheDocument();
    expect(consulta).toHaveBeenCalledWith('FIX-OBRA-A', null);
  });
  it('sin CFDI propone retención por concepto y permite adoptar el importe', async () => {
    render(<CapturarFacturaSheet open onOpenChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.change(screen.getByLabelText('Subtotal'), { target: { value: '1000' } });
    fireEvent.click(screen.getByRole('combobox', { name: 'Concepto fiscal de retención' }));
    fireEvent.click(await screen.findByRole('option', { name: 'HONORARIOS_PF' }));
    expect(await screen.findByText(/Retención propuesta: 100.00/)).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('difieren del catálogo');
    fireEvent.click(screen.getByRole('button', { name: 'Usar retención propuesta' }));
    expect(screen.getByLabelText('Retenciones')).toHaveValue(100);
    expect(screen.queryByText(/Las retenciones difieren del catálogo/)).not.toBeInTheDocument();
  });
});
