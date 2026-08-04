import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { TableSkeleton } from '@/components/erp/feedback/TableSkeleton';

describe('<TableSkeleton>', () => {
  it('renderiza con role status y aria-busy=true', () => {
    render(<TableSkeleton rows={2} />);
    const status = screen.getByRole('status');
    expect(status).toHaveAttribute('aria-busy', 'true');
    expect(status).toHaveAccessibleName(/cargando tabla/i);
  });

  it('respeta el número de filas (header + N filas) × columnas', () => {
    const { container } = render(
      <TableSkeleton
        rows={3}
        columns={[{ width: 'w-24' }, { width: 'w-32' }]}
      />,
    );

    // El primitive Skeleton de shadcn usa `animate-pulse`. Contamos así
    // sin acoplarnos a data-slot (que el registry actual no emite).
    // Esperamos: 1 header (2) + 3 rows × 2 = 8 skeletons.
    const skeletons = container.querySelectorAll('div.animate-pulse');
    expect(skeletons.length).toBe(8);
  });

  it('cae a 5 columnas default cuando no se pasa columns', () => {
    const { container } = render(<TableSkeleton rows={1} />);
    // Header (5) + 1 fila (5) = 10 skeletons.
    expect(container.querySelectorAll('div.animate-pulse').length).toBe(10);
  });
});
