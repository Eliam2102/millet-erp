import { fireEvent, render, screen } from '@testing-library/react';
import { expect, it } from 'vitest';
import { MaestroDetalle } from './MaestroDetalle';

it('alterna la lista sin desmontar filtros o captura', () => {
  render(
    <MaestroDetalle
      seleccionado
      etiquetaLista="facturas"
      lista={
        <aside>
          <input aria-label="Filtro" defaultValue="Proveedor" />
        </aside>
      }
    >
      <section>
        <input aria-label="Referencia" defaultValue="Captura" />
      </section>
    </MaestroDetalle>,
  );
  const filtro = screen.getByLabelText('Filtro');
  const captura = screen.getByLabelText('Referencia');
  const boton = screen.getByRole('button', { name: 'Mostrar facturas' });
  expect(boton).toHaveAttribute('aria-expanded', 'false');
  fireEvent.change(captura, { target: { value: 'Pendiente de guardar' } });
  fireEvent.click(boton);
  expect(boton).toHaveAccessibleName('Ocultar facturas');
  expect(boton).toHaveAttribute('aria-expanded', 'true');
  fireEvent.click(boton);
  expect(screen.getByLabelText('Filtro')).toBe(filtro);
  expect(screen.getByLabelText('Referencia')).toBe(captura);
  expect(captura).toHaveValue('Pendiente de guardar');
  expect(filtro).toHaveValue('Proveedor');
});
it('sin selección mantiene la lista sin control de plegado', () => {
  render(
    <MaestroDetalle seleccionado={false} etiquetaLista="facturas" lista={<aside>Listado</aside>}>
      <section>Selecciona una factura</section>
    </MaestroDetalle>,
  );
  expect(screen.getByText('Listado')).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});
