import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Breadcrumbs } from '@/components/erp/Breadcrumbs';

// Mock del Link de TanStack Router para evitar tener que montar un
// router completo en cada test. Lo serializamos como un <a> que
// preserva `to` y `search` para poder asertar.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    to,
    search,
    children,
    className,
  }: {
    to: string;
    search?: Record<string, unknown>;
    children: React.ReactNode;
    className?: string;
  }) => (
    <a
      href={to}
      data-search={search ? JSON.stringify(search) : undefined}
      className={className}
    >
      {children}
    </a>
  ),
}));

describe('<Breadcrumbs>', () => {
  it('renderiza nada si items es vacío', () => {
    const { container } = render(<Breadcrumbs items={[]} />);
    expect(container.firstChild).toBeNull();
  });

  it('renderiza items con separadores; el último es texto plano (aria-current)', () => {
    render(
      <Breadcrumbs
        items={[
          { label: 'Compras', to: '/compras' },
          { label: 'Requisiciones', to: '/compras/requisiciones' },
          { label: 'MID2026-000042' },
        ]}
      />,
    );

    expect(screen.getByRole('link', { name: 'Compras' })).toHaveAttribute(
      'href',
      '/compras',
    );
    expect(
      screen.getByRole('link', { name: 'Requisiciones' }),
    ).toHaveAttribute('href', '/compras/requisiciones');

    const ultimo = screen.getByText('MID2026-000042');
    expect(ultimo).toHaveAttribute('aria-current', 'page');
    // El último no es link aunque tuviera `to`.
    expect(ultimo.tagName).toBe('SPAN');
  });

  it('preserva search params al pasar `search` (filtros de bandeja)', () => {
    render(
      <Breadcrumbs
        items={[
          {
            label: 'Requisiciones',
            to: '/compras/requisiciones',
            search: { estado: 'EnAutorizacion', offset: 50 },
          },
          { label: 'MID2026-000042' },
        ]}
      />,
    );

    const link = screen.getByRole('link', { name: 'Requisiciones' });
    const search = link.getAttribute('data-search');
    expect(search).toBeDefined();
    const parsed = JSON.parse(search!);
    expect(parsed).toEqual({ estado: 'EnAutorizacion', offset: 50 });
  });

  it('expone aria-label="breadcrumb" en el nav y aria-hidden en chevrons', () => {
    const { container } = render(
      <Breadcrumbs
        items={[
          { label: 'A', to: '/a' },
          { label: 'B' },
        ]}
      />,
    );

    expect(
      container.querySelector('nav[aria-label="breadcrumb"]'),
    ).not.toBeNull();
    // Hay al menos un elemento aria-hidden (el <li> que envuelve el
    // chevron). Lucide-react también marca el SVG como aria-hidden, así
    // que pueden ser ≥ 1.
    expect(
      container.querySelectorAll('[aria-hidden="true"]').length,
    ).toBeGreaterThanOrEqual(1);
  });
});
