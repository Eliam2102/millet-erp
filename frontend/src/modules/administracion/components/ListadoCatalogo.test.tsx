import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ListadoCatalogo } from './ListadoCatalogo';

const items = Array.from({ length: 23 }, (_, i) => ({
  nombre: `Registro ${i + 1}`,
  estado: i % 2,
}));
function Vista() {
  return (
    <ListadoCatalogo
      items={items}
      texto={(item) => item.nombre}
      estado={(item) => item.estado}
      opciones={[
        { valor: 0, label: 'Activo' },
        { valor: 1, label: 'Inactivo' },
      ]}
    >
      {(visibles) => (
        <ul aria-label="Resultados">
          {visibles.map((item) => (
            <li key={item.nombre}>{item.nombre}</li>
          ))}
        </ul>
      )}
    </ListadoCatalogo>
  );
}

describe('Listado local del catálogo', () => {
  it('pagina de diez en diez y conserva la última página parcial', () => {
    render(<Vista />);
    expect(within(screen.getByRole('list')).getAllByRole('listitem')).toHaveLength(10);
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(screen.getByText('Registro 11')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(within(screen.getByRole('list')).getAllByRole('listitem')).toHaveLength(3);
    expect(screen.getByRole('button', { name: 'Página siguiente' })).toBeDisabled();
  });

  it('reinicia la página al buscar y al seleccionar un estado real', () => {
    render(<Vista />);
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Registro 1' } });
    expect(screen.getByText('Registro 1')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Página anterior' })).toBeDisabled();
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '' } });
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Inactivo' }), {
      button: 0,
      ctrlKey: false,
    });
    expect(screen.queryByText('Registro 1')).not.toBeInTheDocument();
    expect(screen.getByText('Registro 2')).toBeVisible();
    expect(screen.getByText('1–10 de 11')).toBeVisible();
  });
});
