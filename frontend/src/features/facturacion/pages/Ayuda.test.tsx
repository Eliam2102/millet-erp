import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { Ayuda } from '@/features/facturacion/pages/Ayuda';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    asChild?: boolean;
  }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));

describe('<Ayuda> — Facturación', () => {
  it('renderiza las secciones principales', () => {
    render(<Ayuda />);
    expect(
      screen.getByRole('heading', { name: /Ayuda — Facturación/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Estados de un pedido/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Estados de timbrado/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Términos del módulo/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Operación de caja por teclado/i }),
    ).toBeInTheDocument();
  });

  it('cero violations WCAG 2.1 AA', async () => {
    const { container } = render(<Ayuda />);
    const results: AxeResults = await axe.run(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id}: ${v.help}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
    expect(results.violations).toHaveLength(0);
  });
});
