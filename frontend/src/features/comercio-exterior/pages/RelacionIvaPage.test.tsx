import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { RelacionIvaPage } from './RelacionIvaPage';
const state = vi.hoisted(() => ({ consulta: vi.fn(), excel: vi.fn(), pdf: vi.fn() }));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: ({ value, onChange }: { value?: string; onChange: (id: string) => void }) => (
    <select aria-label="Sucursal" value={value ?? ''} onChange={(e) => onChange(e.target.value)}>
      <option value="mid">Conkal</option>
      <option value="cir">Circuito</option>
    </select>
  ),
}));
beforeEach(() => {
  useAuthStore.setState({ currentSucursalId: 'mid', permisos: [] });
  vi.clearAllMocks();
});
afterEach(() => useAuthStore.setState({ currentSucursalId: null, permisos: [] }));
vi.mock('@/components/erp', () => ({ Breadcrumbs: () => null }));
vi.mock('../api/hooks', () => ({
  useRelacionIva: (anio: number, mes: number, sucursal?: string) => {
    state.consulta(anio, mes, sucursal);
    return {
      data: {
        titulo: 'Relación para IVA',
        generadoEn: '2026-10-16T12:00:00Z',
        filtrosAplicados: { Periodo: '2026-10' },
        columnas: [{ clave: 'pedido', etiqueta: 'Pedido', tipo: 'texto' }],
        filas: Array.from({ length: 12 }, (_, i) => ({ pedido: `DEMO-PED-${i + 1}` })),
      },
      isLoading: false,
      error: null,
    };
  },
}));
vi.mock('@/components/erp/reportes/exporters/reporte-excel-exporter', () => ({
  descargarReporteExcel: state.excel,
}));
vi.mock('@/components/erp/reportes/exporters/reporte-pdf-exporter', () => ({
  descargarReportePdf: state.pdf,
}));
describe('Relación para IVA', () => {
  it('filtra por mes, pagina diez filas y exporta el periodo completo a Excel', async () => {
    render(<RelacionIvaPage />);
    expect(screen.queryByRole('button', { name: 'Todas' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(11);
    expect(screen.queryByText('DEMO-PED-11')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(screen.getByText('DEMO-PED-11')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Excel' }));
    await waitFor(() =>
      expect(state.excel).toHaveBeenCalledWith(
        expect.objectContaining({ filas: expect.any(Array) }),
        expect.any(String),
      ),
    );
    expect(state.excel.mock.calls[0][0].filas).toHaveLength(12);
    fireEvent.change(screen.getByLabelText('Periodo'), { target: { value: '2026-09' } });
    expect(state.consulta).toHaveBeenLastCalledWith(2026, 9, 'mid');
    expect(screen.getByText('DEMO-PED-1')).toBeInTheDocument();
  });
  it('permite Todas sólo con permiso y envía la sucursal seleccionada', () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorDocumentosLeerTodasSucursales] });
    render(<RelacionIvaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Todas' }));
    expect(state.consulta.mock.lastCall?.[2]).toBeUndefined();
    fireEvent.change(screen.getByLabelText('Sucursal'), { target: { value: 'cir' } });
    expect(state.consulta.mock.lastCall?.[2]).toBe('cir');
  });
});
