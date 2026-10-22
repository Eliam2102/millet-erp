import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AnalisisComprasPage } from './AnalisisComprasPage';

vi.mock('@/components/auth/EmpresaSelector', () => ({
  EmpresaSelector: () => <span>Empresa DEMO ficticia</span>,
}));
vi.mock('@/components/erp/selectors/ProveedorSelector', () => ({
  ProveedorSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <select aria-label="Proveedor" onChange={(e) => onChange(e.target.value)}>
      <option value="">Todos</option>
      <option value="demo-b">DEMO B</option>
    </select>
  ),
}));
vi.mock('@/components/erp/selectors/ArticuloSelector', () => ({
  ArticuloSelector: () => <span>Artículo</span>,
}));
vi.mock('@/components/erp/selectors/DepartamentoSelector', () => ({
  DepartamentoSelector: () => <span>Departamento</span>,
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: () => <span>Sucursal</span>,
}));

const exportar = vi.hoisted(() => vi.fn());
vi.mock('@/components/erp/reportes/exporters/reporte-excel-exporter', () => ({
  descargarReporteExcel: exportar,
}));

const fila = {
  ordenCompraId: 'oc-demo',
  lineaId: 'linea-demo',
  folio: 'OC-DEMO-1',
  proveedorId: 'demo-a',
  proveedor: 'DEMO A',
  articulo: 'Artículo ficticio',
  departamento: 'Departamento ficticio',
  moneda: 'MXN',
  cantidad: 10,
  recibidoNeto: 8,
  porRecibir: 2,
  porFacturar: 4,
  porPagar: 42,
  importeCompra: 100,
  diferenciaPrecio: 2,
  incidencias: 1,
  compromiso: '2026-01-20',
  diasAtraso: 70,
  movimientos: [
    {
      documentoId: 'rec-demo',
      folio: 'REC-DEMO-1',
      fecha: '2026-01-15',
      cantidad: 10,
      esDevolucion: false,
    },
    {
      documentoId: 'dev-demo',
      folio: 'DEV-DEMO-1',
      fecha: '2026-02-01',
      cantidad: 2,
      esDevolucion: true,
    },
  ],
};
function responder(vacio = false) {
  const solicitudes: URL[] = [];
  mswServer.use(
    http.get('*/api/v1/compras/ordenes/analisis', ({ request }) => {
      solicitudes.push(new URL(request.url));
      return HttpResponse.json({
        generadoEn: '2026-03-31T12:00:00Z',
        partidas: vacio ? [] : [fila],
        proveedores: vacio
          ? []
          : [
              {
                proveedorId: 'demo-a',
                proveedor: 'DEMO A',
                moneda: 'MXN',
                partidas: 1,
                porcentajeATiempo: 0,
                porcentajeCompleto: 0,
                incidencias: 1,
                importeCompra: 100,
                porPagar: 42,
                diferenciaPrecio: 2,
                lineasIds: ['linea-demo'],
              },
            ],
      });
    }),
  );
  return solicitudes;
}
async function consultar() {
  fireEvent.change(screen.getByLabelText('Desde'), { target: { value: '2026-01-01' } });
  fireEvent.change(screen.getByLabelText('Hasta'), { target: { value: '2026-03-31' } });
  fireEvent.click(screen.getByRole('button', { name: 'Consultar' }));
}
describe('Análisis de compras — DEMO ficticio', () => {
  it('COM-13-a/c/e: indicadores, saldos y documentos de cada total', async () => {
    responder();
    render(<AnalisisComprasPage />, { wrapper: createQueryWrapper() });
    await consultar();
    expect(await screen.findByText('DEMO A')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver sustento de DEMO A/ }));
    expect(await screen.findByRole('link', { name: 'OC-DEMO-1' })).toHaveAttribute(
      'href',
      '/compras/ordenes/oc-demo',
    );
    expect(screen.getByRole('link', { name: 'REC-DEMO-1' })).toHaveAttribute(
      'href',
      '/almacen/recepciones/rec-demo',
    );
    expect(screen.getByRole('link', { name: 'DEV-DEMO-1' })).toHaveAttribute(
      'href',
      '/almacen/devoluciones/proveedor/dev-demo',
    );
    expect(screen.getByText('Por recibir')).toBeInTheDocument();
  });
  it('COM-13-b: conjunto vacío no inventa proveedores', async () => {
    responder(true);
    render(<AnalisisComprasPage />, { wrapper: createQueryWrapper() });
    await consultar();
    expect(await screen.findByText('Sin datos para los filtros aplicados.')).toBeInTheDocument();
    expect(screen.queryByText('DEMO A')).not.toBeInTheDocument();
  });
  it('COM-13-d: proveedor y periodo cambian la consulta y limpian el sustento', async () => {
    const solicitudes = responder();
    render(<AnalisisComprasPage />, { wrapper: createQueryWrapper() });
    await consultar();
    await screen.findByText('DEMO A');
    fireEvent.change(screen.getByLabelText('Proveedor'), { target: { value: 'demo-b' } });
    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: '2026-02-01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Consultar' }));
    await waitFor(() => expect(solicitudes.at(-1)?.searchParams.get('proveedorId')).toBe('demo-b'));
    expect(solicitudes.at(-1)?.searchParams.get('desde')).toBe('2026-02-01');
    expect(screen.queryByRole('link', { name: 'OC-DEMO-1' })).not.toBeInTheDocument();
  });
  it('exporta el mismo sustento con recepciones y devoluciones legibles', async () => {
    responder();
    render(<AnalisisComprasPage />, { wrapper: createQueryWrapper() });
    await consultar();
    fireEvent.click(await screen.findByRole('button', { name: /Ver sustento de DEMO A/ }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Excel' })[1]);
    await waitFor(() => expect(exportar).toHaveBeenCalled());
    const exportado = exportar.mock.calls.at(-1)?.[0];
    expect(exportado.filas[0].documentos).toContain('REC-DEMO-1');
    expect(exportado.filas[0].documentos).toContain('DEV-DEMO-1');
    expect(exportado.filas[0].porPagar).toBe(42);
  });
});
