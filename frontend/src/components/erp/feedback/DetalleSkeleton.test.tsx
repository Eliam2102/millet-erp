import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DetalleSkeleton } from './DetalleSkeleton';

describe('DetalleSkeleton', () => {
  it('expone estado de carga y una dl con los campos pedidos', () => {
    const { container } = render(<DetalleSkeleton campos={4} />);
    expect(screen.getByRole('status', { name: 'Cargando detalle' })).toBeInTheDocument();
    expect(container.querySelectorAll('dl > div')).toHaveLength(4);
  });
});
