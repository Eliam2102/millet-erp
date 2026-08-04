import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CubrimientoEstimadoPanel } from '@/features/compras/components/CubrimientoEstimadoPanel';
import type { PreviewCubrimientoLinea } from '@/features/compras/api/types';

const LINEAS: PreviewCubrimientoLinea[] = [
  {
    lineaId: 'l-1',
    articuloId: 'a-1',
    articuloClave: null,
    articuloNombre: null,
    cantidad: 80,
    estimadoDeAlmacen: 50,
    estimadoDeCompra: 30,
    disponible: 50,
  },
  {
    lineaId: 'l-2',
    articuloId: 'a-2',
    articuloClave: null,
    articuloNombre: null,
    cantidad: 5,
    estimadoDeAlmacen: 0,
    estimadoDeCompra: 5,
    disponible: 0,
  },
];

describe('<CubrimientoEstimadoPanel>', () => {
  it('muestra header, el label "no reserva stock" y el estimado por línea', () => {
    const { container } = render(<CubrimientoEstimadoPanel lineas={LINEAS} />);
    expect(screen.getByText('Disponibilidad estimada')).toBeInTheDocument();
    // El label deja claro que es estimación y que NO reserva.
    expect(screen.getByText(/no reserva stock/i)).toBeInTheDocument();
    expect(
      container.querySelector('[data-cubrimiento-estimado]'),
    ).not.toBeNull();
    expect(screen.getByText('50 disp · 30 a comprar')).toBeInTheDocument();
    expect(screen.getByText('0 disp · 5 a comprar')).toBeInTheDocument();
  });

  it('resuelve el nombre del artículo con resolverArticulo', () => {
    render(
      <CubrimientoEstimadoPanel
        lineas={LINEAS}
        resolverArticulo={(id) => `ART ${id}`}
      />,
    );
    expect(screen.getByText('ART a-1')).toBeInTheDocument();
  });

  it('loading: muestra skeleton, no la lista de líneas', () => {
    const { container } = render(
      <CubrimientoEstimadoPanel lineas={[]} isLoading />,
    );
    expect(container.querySelector('[data-linea-estimada]')).toBeNull();
    // El header y el disclaimer siguen visibles en loading.
    expect(screen.getByText('Disponibilidad estimada')).toBeInTheDocument();
  });

  it('usa la etiqueta enriquecida del DTO (clave · nombre) por encima del resolver', () => {
    // El fix: la línea trae clave/nombre server-side (ADR-0042) → el panel ya
    // NO depende del catálogo capado. El DTO gana sobre el resolver.
    const lineas: PreviewCubrimientoLinea[] = [
      {
        lineaId: 'l-1',
        articuloId: 'a-1',
        articuloClave: 'MUO00003',
        articuloNombre: 'ALMOADILLA O COJIN PARA SELLO No.1',
        cantidad: 5,
        estimadoDeAlmacen: 0,
        estimadoDeCompra: 5,
        disponible: 0,
      },
    ];
    render(
      <CubrimientoEstimadoPanel
        lineas={lineas}
        resolverArticulo={() => 'NO-DEBERIA-USARSE'}
      />,
    );
    expect(
      screen.getByText('MUO00003 · ALMOADILLA O COJIN PARA SELLO No.1'),
    ).toBeInTheDocument();
    expect(screen.queryByText('NO-DEBERIA-USARSE')).not.toBeInTheDocument();
  });

  it('DTO con clave/nombre null y sin resolver → cae al id crudo', () => {
    // Fallback final: sin etiqueta DTO ni resolver, muestra el articuloId.
    render(<CubrimientoEstimadoPanel lineas={LINEAS} />);
    expect(screen.getByText('a-1')).toBeInTheDocument();
    expect(screen.getByText('a-2')).toBeInTheDocument();
  });
});
