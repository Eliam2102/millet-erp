import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteConsumoInsumosLineaPage } from './ReporteConsumoInsumosLineaPage';

const reporte = {
  titulo: 'CONSUMO-INSUMOS-LINEA',
  generadoEn: '2027-03-31T18:00:00Z',
  filtrosAplicados: { desde: '2027-03-01', hasta: '2027-03-31' },
  columnas: [
    { clave: 'linea', etiqueta: 'Línea', tipo: 'texto' },
    { clave: 'articuloClave', etiqueta: 'Artículo', tipo: 'texto' },
    { clave: 'cantidad', etiqueta: 'Cantidad', tipo: 'numero', alineacion: 'right' },
    { clave: 'costoTotalMxn', etiqueta: 'Costo total', tipo: 'moneda', alineacion: 'right' },
  ],
  filas: [{ linea: 'LIN-1 · Línea laminado', articuloClave: 'PVB', cantidad: 2, costoTotalMxn: 3000 }],
  totales: { costoTotalMxn: 3000 },
};

describe('<ReporteConsumoInsumosLineaPage>', () => {
  it('consulta con el periodo y muestra la línea con su consumo', async () => {
    const peticiones: string[] = [];
    mswServer.use(
      http.get('*/api/v1/almacen/reportes/consumo-insumos-linea', ({ request }) => {
        peticiones.push(request.url);
        return HttpResponse.json(reporte);
      }),
    );
    render(<ReporteConsumoInsumosLineaPage />, { wrapper: createQueryWrapper() });

    expect((await screen.findAllByText('LIN-1 · Línea laminado')).length).toBeGreaterThan(0);
    expect(peticiones[0]).toMatch(/desde=\d{4}-\d{2}-01/);
  });
});
