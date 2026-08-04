import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaOcSelector } from '@/components/erp/selectors/LineaOcSelector';

const OC_ID = '11111111-1111-4111-8111-111111111111';
const LINEA_A = '22222222-2222-4222-8222-222222222222';
const LINEA_B = '33333333-3333-4333-8333-333333333333';

// Detalle mínimo de OC: el componente solo lee `.lineas`.
function mockOcDetalle() {
  mswServer.use(
    http.get('*/api/v1/compras/ordenes/:id', () =>
      HttpResponse.json({
        id: OC_ID,
        lineas: [
          {
            id: LINEA_A,
            posicion: 1,
            articuloId: 'art-a',
            articuloClave: 'TORN-001',
            articuloNombre: 'Tornillo',
            cantidad: 10,
            unidadMedida: 'PZA',
            precioUnitario: 5,
            cantidadFacturada: 4,
            cantidadRecibida: 10,
          },
          {
            id: LINEA_B,
            posicion: 2,
            articuloId: 'art-b',
            articuloClave: 'TUER-002',
            articuloNombre: 'Tuerca',
            cantidad: 5,
            unidadMedida: 'PZA',
            precioUnitario: 2,
            cantidadFacturada: 0,
            cantidadRecibida: 0,
          },
        ],
      }),
    ),
  );
}

describe('<LineaOcSelector>', () => {
  it('sin ordenCompraId, el trigger está disabled', () => {
    render(
      <LineaOcSelector ordenCompraId={null} value={null} onChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByRole('combobox')).toBeDisabled();
  });

  it('con ordenCompraId, lista las líneas de la OC (artículo + posición, no el GUID)', async () => {
    mockOcDetalle();
    render(
      <LineaOcSelector ordenCompraId={OC_ID} value={null} onChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.click(screen.getByRole('combobox'));

    expect(await screen.findByText('TORN-001 · Tornillo')).toBeInTheDocument();
    expect(screen.getByText('TUER-002 · Tuerca')).toBeInTheDocument();
    // Las opciones muestran etiqueta útil, no el GUID de la línea.
    expect(screen.queryByText(LINEA_A)).not.toBeInTheDocument();
  });

  it('al elegir una línea dispara onChange con su id', async () => {
    const onChange = vi.fn();
    mockOcDetalle();
    render(
      <LineaOcSelector ordenCompraId={OC_ID} value={null} onChange={onChange} />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByText('TORN-001 · Tornillo'));

    expect(onChange).toHaveBeenCalledWith(LINEA_A);
  });
});
