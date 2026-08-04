import { describe, expect, it, vi } from 'vitest';
import { render } from '@testing-library/react';
import { EntidadReordenSelector } from '@/components/erp/selectors/EntidadReordenSelector';
import { NivelReorden } from '@/features/almacen/api/types';

// Stubs de los selectores hijos: aíslan el test del react-query de catálogos.
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: () => <div data-testid="sucursal-selector" />,
}));
vi.mock('@/components/erp/selectors/AlmacenSelector', () => ({
  AlmacenSelector: () => <div data-testid="almacen-selector" />,
}));

describe('<EntidadReordenSelector>', () => {
  it('renderiza el selector según el nivel (Almacén → almacén, Sucursal → sucursal)', () => {
    const { getByTestId, queryByTestId, rerender } = render(
      <EntidadReordenSelector
        nivel={NivelReorden.Almacen}
        value={null}
        onChange={() => {}}
      />,
    );
    expect(getByTestId('almacen-selector')).toBeInTheDocument();
    expect(queryByTestId('sucursal-selector')).not.toBeInTheDocument();

    rerender(
      <EntidadReordenSelector
        nivel={NivelReorden.Sucursal}
        value={null}
        onChange={() => {}}
      />,
    );
    expect(getByTestId('sucursal-selector')).toBeInTheDocument();
    expect(queryByTestId('almacen-selector')).not.toBeInTheDocument();
  });

  it('resetea la entidad al CAMBIAR de nivel, pero NO en el montaje inicial', () => {
    const onChange = vi.fn();
    const { rerender } = render(
      <EntidadReordenSelector
        nivel={NivelReorden.Almacen}
        value="alm-1"
        onChange={onChange}
      />,
    );
    // Montaje: preserva el value (caso edición, nivel inmutable).
    expect(onChange).not.toHaveBeenCalled();

    // Cambio de nivel → resetea la entidad a null.
    rerender(
      <EntidadReordenSelector
        nivel={NivelReorden.Sucursal}
        value="alm-1"
        onChange={onChange}
      />,
    );
    expect(onChange).toHaveBeenCalledWith(null);
  });
});
