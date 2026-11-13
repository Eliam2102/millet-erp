import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow,
} from './table';

describe('Table', () => {
  it('renderiza contenedor con scroll y header sticky', () => {
    render(
      <TableContainer data-testid="c">
        <Table>
          <TableHeader data-testid="h">
            <TableRow><TableHead>A</TableHead></TableRow>
          </TableHeader>
          <TableBody><TableRow><TableCell>1</TableCell></TableRow></TableBody>
        </Table>
      </TableContainer>,
    );
    expect(screen.getByTestId('c').className).toContain('overflow-auto');
    expect(screen.getByTestId('c').className).not.toContain('100vh');
    expect(screen.getByTestId('h').className).toContain('sticky');
  });
  it('pagina una colección de consulta y conserva todas las filas de una tabla editable', () => {
    localStorage.clear();
    const filas = Array.from({ length: 32 }, (_, i) => <TableRow key={i}><TableCell>Registro {i + 1}</TableCell></TableRow>);
    const v = render(<Table paginada><TableBody>{filas}</TableBody></Table>);
    expect(screen.getAllByRole('row')).toHaveLength(10);
    fireEvent.change(screen.getByLabelText('Filas por página'), { target: { value: '20' } });
    expect(screen.getAllByRole('row')).toHaveLength(20);
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(screen.getAllByRole('row')).toHaveLength(12);
    expect(screen.getByText('21–32 de 32')).toBeInTheDocument();
    v.rerender(<Table><TableBody>{filas}</TableBody></Table>);
    expect(screen.getAllByRole('row')).toHaveLength(32);
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });
});
