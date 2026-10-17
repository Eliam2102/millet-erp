import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow } from './table';

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
    expect(screen.getByTestId('c').className).toContain('max-h-[calc(100vh-14rem)]');
    expect(screen.getByTestId('h').className).toContain('sticky');
  });
});
