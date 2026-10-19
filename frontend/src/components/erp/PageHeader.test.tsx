import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PageHeader } from '@/components/erp/PageHeader';

describe('PageHeader', () => {
  it('renderiza H1, descripción y acciones', () => {
    render(
      <PageHeader titulo="Requisiciones" descripcion="Todas las RQ." acciones={<button>Nueva</button>} />,
    );
    expect(screen.getByRole('heading', { level: 1, name: 'Requisiciones' })).toHaveClass('text-3xl');
    expect(screen.getByText('Todas las RQ.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nueva' })).toBeInTheDocument();
  });

  it('omite descripción y acciones si no se pasan', () => {
    const { container } = render(<PageHeader titulo="X" />);
    expect(container.querySelector('p')).toBeNull();
  });

  it('muestra el estado junto al H1 y aplica mono', () => {
    render(<PageHeader titulo="F-1" mono estado={<span>Borrador</span>} />);
    expect(screen.getByRole('heading', { level: 1, name: 'F-1' })).toHaveClass('font-mono');
    expect(screen.getByText('Borrador')).toBeInTheDocument();
  });
});
