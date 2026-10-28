import { expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { DepreciacionFiscalPage } from './DepreciacionFiscalPage';
const state = vi.hoisted(() => ({ exportar: vi.fn(), consultar: vi.fn() }));
vi.mock('../api/libro', () => ({
  useLibroQuery: (ruta: string) => {
    state.consultar(ruta);
    return {
      data: {
        titulo: 'Depreciación fiscal',
        generadoEn: '2026-01-01T00:00:00Z',
        filtrosAplicados: { Ejercicio: '2026' },
        columnas: [
          { clave: 'numero', etiqueta: 'Activo', tipo: 'texto' },
          { clave: 'factor', etiqueta: 'Factor INPC', tipo: 'numero', decimales: 4 },
        ],
        filas: [
          {
            numero: 'FICTICIO-P12',
            clase: '154',
            factor: 1.03,
            deduccionHistorica: 114583.33,
            deduccionActualizada: 118020.83,
          },
        ],
        totales: { '154.deduccionHistorica': 114583.33, '154.deduccionActualizada': 118020.83 },
      },
      isLoading: false,
      error: null,
    };
  },
}));
vi.mock('@/components/erp/reportes/exporters/reporte-excel-exporter', () => ({
  descargarReporteExcel: state.exportar,
}));
it('consulta corte y exporta filas, factor y totales por clase mediante ReporteShell', async () => {
  render(<DepreciacionFiscalPage />);
  fireEvent.change(screen.getByLabelText('Ejercicio y mes de corte'), {
    target: { value: '2026-12' },
  });
  expect(state.consultar).toHaveBeenLastCalledWith(
    'reportes/depreciacion-fiscal?ejercicio=2026&mesCorte=12',
  );
  expect(screen.getByText('FICTICIO-P12')).toBeInTheDocument();
  expect(screen.getByText('1.0300')).toBeInTheDocument();
  fireEvent.click(screen.getAllByRole('button', { name: 'Excel' })[0]);
  await waitFor(() =>
    expect(state.exportar).toHaveBeenCalledWith(
      expect.objectContaining({
        filas: expect.arrayContaining([
          expect.objectContaining({ numero: 'FICTICIO-P12', factor: 1.03 }),
          expect.objectContaining({
            numero: 'Total clase 154',
            deduccionHistorica: 114583.33,
            deduccionActualizada: 118020.83,
          }),
        ]),
      }),
      'depreciacion-fiscal-2026-12',
    ),
  );
});
