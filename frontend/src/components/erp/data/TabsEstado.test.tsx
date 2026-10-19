import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { TabsEstado } from '@/components/erp/data/TabsEstado';
import { BarraFiltros } from '@/components/erp/data/BarraFiltros';

const OPCIONES = [
  { valor: 1, label: 'Borrador' },
  { valor: 2, label: 'Registrado' },
];

describe('<TabsEstado>', () => {
  it('muestra el conteo por estado, la suma en «Todos» y 0 para los estados ausentes', () => {
    render(<TabsEstado opciones={OPCIONES} valor={undefined} onChange={() => {}} conteos={{ 2: 5 }} />);
    expect(screen.getByRole('tab', { name: /Todos/ })).toHaveTextContent('5');
    expect(screen.getByRole('tab', { name: /Borrador/ })).toHaveTextContent('0');
    expect(screen.getByRole('tab', { name: /Registrado/ })).toHaveTextContent('5');
  });

  it('no muestra números mientras los conteos cargan y avisa el estado elegido', () => {
    const onChange = vi.fn();
    render(<TabsEstado opciones={OPCIONES} valor={undefined} onChange={onChange} conteos={undefined} />);
    expect(screen.getByRole('tab', { name: 'Registrado' })).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Registrado' }), { button: 0, ctrlKey: false });
    expect(onChange).toHaveBeenCalledWith(2);
  });
});

describe('<BarraFiltros>', () => {
  it('oculta los filtros secundarios tras «Más filtros» y limpia', () => {
    const onLimpiar = vi.fn();
    render(
      <BarraFiltros
        busqueda={<input aria-label="Buscar" />}
        masFiltros={<span>Filtro secundario</span>}
        masFiltrosActivos={1}
        onLimpiar={onLimpiar}
        total="3 cosas"
      />,
    );
    expect(screen.queryByText('Filtro secundario')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Más filtros' }));
    expect(screen.getByText('Filtro secundario')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    expect(onLimpiar).toHaveBeenCalled();
    expect(screen.getByText('3 cosas')).toBeInTheDocument();
  });
});
