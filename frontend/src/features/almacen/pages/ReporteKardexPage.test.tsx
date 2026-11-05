import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteKardexPage } from './ReporteKardexPage';

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  ArticuloSelector: ({ onChange }: { onChange: (id: string | null) => void }) => (
    <button onClick={() => onChange('art-1')}>Elegir artículo</button>
  ),
}));

const reporte = {
  titulo: "Kardex de almacén",
  generadoEn: '2027-10-31T18:00:00Z',
  filtrosAplicados: { articulo: 'SIL-01 · Silicón estructural', saldo_final: '24', saldo_cierre: '24', cuadra_con_cierre: 'sí' },
  columnas: [
    { clave: 'tipo', etiqueta: 'Tipo de movimiento', tipo: 'texto' },
    { clave: 'saldo', etiqueta: 'Saldo', tipo: 'numero', alineacion: 'right' },
  ],
  filas: [
    { tipo: 'Saldo inicial', saldo: 10 },
    { tipo: 'Ajuste negativo', saldo: 24 },
  ],
  totales: { saldo: 24 },
};

describe('<ReporteKardexPage>', () => {
  it('sin artículo no consulta; con artículo muestra el kardex y manda el periodo', async () => {
    const peticiones: string[] = [];
    mswServer.use(
      http.get('*/api/v1/almacen/sub-almacenes', () => HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 })),
      http.get('*/api/v1/almacen/ubicaciones', () => HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 })),
      http.get('*/api/v1/almacen/reportes/kardex', ({ request }) => {
        peticiones.push(request.url);
        return HttpResponse.json(reporte);
      }),
    );
    render(<ReporteKardexPage />, { wrapper: createQueryWrapper() });

    expect(screen.getByText('Elige un artículo para ver su kardex.')).toBeInTheDocument();
    expect(peticiones).toHaveLength(0);

    fireEvent.click(screen.getByText('Elegir artículo'));

    expect((await screen.findAllByText('Saldo inicial')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Ajuste negativo').length).toBeGreaterThan(0);
    expect(screen.getByText(/cuadra_con_cierre: sí/)).toBeInTheDocument();
    expect(peticiones.at(-1)).toContain('articuloId=art-1');
  });
});
