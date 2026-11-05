import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteTrazabilidadMpPage } from './ReporteTrazabilidadMpPage';

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  ArticuloSelector: ({ onChange }: { onChange: (id: string | null) => void }) => (
    <button onClick={() => onChange('art-1')}>Elegir artículo</button>
  ),
}));

const reporte = {
  titulo: 'TRAZABILIDAD-MP',
  generadoEn: '2027-03-31T18:00:00Z',
  filtrosAplicados: { articulo: 'VID-6 · Vidrio flotado 6 mm', lote: 'L-100' },
  columnas: [
    { clave: 'concepto', etiqueta: 'Concepto', tipo: 'texto' },
    { clave: 'referencia', etiqueta: 'OC / orden / motivo', tipo: 'texto' },
    { clave: 'saldo', etiqueta: 'Saldo', tipo: 'numero', alineacion: 'right' },
  ],
  filas: [
    { concepto: 'Compra', referencia: 'OC OC-77', saldo: 50 },
    { concepto: 'Consumo', referencia: 'Orden A+W 123', saldo: 40 },
  ],
  totales: { saldo: 40 },
};

describe('<ReporteTrazabilidadMpPage>', () => {
  it('sin artículo no consulta; con artículo y lote muestra compra, consumo y saldo', async () => {
    const peticiones: string[] = [];
    mswServer.use(
      http.get('*/api/v1/almacen/sub-almacenes', () => HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 })),
      http.get('*/api/v1/almacen/reportes/trazabilidad-mp', ({ request }) => {
        peticiones.push(request.url);
        return HttpResponse.json(reporte);
      }),
    );
    render(<ReporteTrazabilidadMpPage />, { wrapper: createQueryWrapper() });

    expect(screen.getByText('Elige un artículo para ver su trazabilidad.')).toBeInTheDocument();
    expect(peticiones).toHaveLength(0);

    fireEvent.click(screen.getByText('Elegir artículo'));
    fireEvent.change(screen.getByLabelText('Lote (opcional)'), { target: { value: 'L-100' } });

    expect((await screen.findAllByText('OC OC-77')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Orden A+W 123').length).toBeGreaterThan(0);
    const ultima = peticiones.at(-1)!;
    expect(ultima).toContain('articuloId=art-1');
    expect(ultima).toContain('lote=L-100');
  });
});
