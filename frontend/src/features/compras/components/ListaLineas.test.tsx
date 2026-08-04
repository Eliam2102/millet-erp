import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ListaLineas } from '@/features/compras/components/ListaLineas';
import type { LineaResponse } from '@/features/compras/api/types';

function makeLinea(overrides: Partial<LineaResponse> = {}): LineaResponse {
  return {
    id: 'l-1',
    posicion: 1,
    articuloId: 'art-1',
    cantidad: 10,
    unidadMedida: 'PZA',
    precioEstimadoMonto: 100,
    precioEstimadoMoneda: 'MXN',
    cuentaContableId: null,
    centroCostoId: null,
    centroCostoClave: null,
    centroCostoNombre: null,
    proyecto: null,
    fechaRequerida: null,
    notas: null,
    cantDeAlmacen: 0,
    cantDeCompra: 0,
    cantRecibida: 0,
    cantPendiente: 10,
    reservaId: null,
    ...overrides,
  };
}

describe('<ListaLineas>', () => {
  it('muestra empty state cuando no hay líneas', () => {
    render(<ListaLineas lineas={[]} />);
    expect(screen.getByText(/sin líneas registradas/i)).toBeInTheDocument();
  });

  it('renderiza tabla con cantidad, UM, precio y cubrimiento', () => {
    render(<ListaLineas lineas={[makeLinea()]} />);
    expect(screen.getByText('PZA')).toBeInTheDocument();
    expect(screen.getByText('$100.00')).toBeInTheDocument();
    expect(screen.getAllByText(/10/)[0]).toBeInTheDocument();
  });

  it('ordena por posición ascendente', () => {
    render(
      <ListaLineas
        lineas={[
          makeLinea({ id: 'l-3', posicion: 3, articuloId: 'art-3' }),
          makeLinea({ id: 'l-1', posicion: 1, articuloId: 'art-1' }),
          makeLinea({ id: 'l-2', posicion: 2, articuloId: 'art-2' }),
        ]}
      />,
    );
    // El primer "Articulo" en el DOM debe ser art-1.
    const articulos = screen.getAllByText(/art-/);
    expect(articulos[0]).toHaveTextContent('art-1');
    expect(articulos[1]).toHaveTextContent('art-2');
    expect(articulos[2]).toHaveTextContent('art-3');
  });

  it('aplica resolverArticulo si se provee', () => {
    render(
      <ListaLineas
        lineas={[makeLinea({ articuloId: 'art-99' })]}
        resolverArticulo={(id) => `Tornillo ${id}`}
      />,
    );
    expect(screen.getByText('Tornillo art-99')).toBeInTheDocument();
  });

  it('muestra notas de la línea cuando están presentes', () => {
    render(
      <ListaLineas
        lineas={[makeLinea({ notas: 'Urgente; entregar mañana' })]}
      />,
    );
    expect(
      screen.getByText('Urgente; entregar mañana'),
    ).toBeInTheDocument();
  });

  it('CC-Máquina: resoluble ("clave — nombre"), irresoluble ("No catalogado") y sin CC ("—")', () => {
    render(
      <ListaLineas
        lineas={[
          makeLinea({
            id: 'l-1',
            posicion: 1,
            centroCostoId: 'm-1',
            centroCostoClave: 'MCLC101',
            centroCostoNombre: 'Gantry',
          }),
          // id presente pero el read-port no lo resolvió (histórico roto).
          makeLinea({ id: 'l-2', posicion: 2, centroCostoId: 'm-x' }),
          // sin CC en la línea.
          makeLinea({ id: 'l-3', posicion: 3, centroCostoId: null }),
        ]}
      />,
    );
    expect(screen.getByText('MCLC101 — Gantry')).toBeInTheDocument();
    expect(screen.getByText('No catalogado')).toBeInTheDocument();
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(1);
  });
});
