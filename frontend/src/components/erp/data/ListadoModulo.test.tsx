import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ListadoModulo } from './ListadoModulo';
describe('ListadoModulo', () => {
  it('ordena slots y permite scroll de la tabla', () => {
    const { container } = render(
      <ListadoModulo
        tabs={<nav>Vistas</nav>}
        filtros={<input aria-label="Buscar" />}
        pie={<footer>Página 1</footer>}
      >
        <table>
          <tbody>
            <tr>
              <td>Factura</td>
            </tr>
          </tbody>
        </table>
      </ListadoModulo>,
    );
    const slots = container.firstElementChild!.children;
    expect(slots[0]).toContainElement(screen.getByRole('navigation'));
    expect(slots[1]).toContainElement(screen.getByRole('textbox'));
    expect(slots[2]).toContainElement(screen.getByRole('table'));
    expect(slots[2]).toHaveClass('overflow-auto');
    expect(slots[3]).toHaveTextContent('Página 1');
  });
});
