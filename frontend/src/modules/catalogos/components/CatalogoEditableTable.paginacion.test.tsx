import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { CatalogoEditableTable } from './CatalogoEditableTable';

const items = Array.from({ length: 23 }, (_, i) => ({
  id: String(i),
  clave: `DEMO-${i + 1}`,
  nombre: `Registro ${i + 1}`,
  estatus: (i % 2) as 0 | 1,
}));
describe('Catálogo: navegación sobre los registros recibidos', () => {
  it('pagina de diez en diez y reinicia al filtrar por búsqueda o estado', () => {
    render(
      <CatalogoEditableTable
        recurso="registros"
        recursoSingular="Registro"
        items={items}
        isLoading={false}
        isError={false}
        error={null}
        onRetry={() => {}}
        columns={[
          { key: 'clave', label: 'Clave' },
          { key: 'nombre', label: 'Nombre' },
        ]}
        renderInlineEditForm={() => null}
        desactivar={{ mutate: () => {}, isPending: false }}
        canEditar={false}
        canDesactivar={false}
      />,
    );
    expect(screen.getAllByRole('row')).toHaveLength(11);
    expect(screen.getByText('1–10 de 23')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(screen.queryByText('DEMO-1')).not.toBeInTheDocument();
    expect(screen.getByText('DEMO-11')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(screen.getByText('21–23 de 23')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(4);
    fireEvent.change(screen.getByRole('textbox', { name: 'Buscar en el catálogo' }), {
      target: { value: 'DEMO-23' },
    });
    expect(screen.getByText('1–1 de 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Página anterior' })).toBeDisabled();
    fireEvent.change(screen.getByRole('textbox', { name: 'Buscar en el catálogo' }), {
      target: { value: '' },
    });
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Inactivo' }), {
      button: 0,
      ctrlKey: false,
    });
    expect(screen.queryByText('DEMO-1')).not.toBeInTheDocument();
    expect(screen.getByText('DEMO-2')).toBeInTheDocument();
    expect(screen.getByText('1–10 de 11')).toBeInTheDocument();
  });
});
