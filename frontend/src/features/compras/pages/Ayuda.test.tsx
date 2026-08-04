import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { Ayuda } from '@/features/compras/pages/Ayuda';

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
    search?: unknown;
  }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));

describe('<Ayuda> — smoke', () => {
  it('renderiza las 6 secciones principales', () => {
    render(<Ayuda />);
    expect(
      screen.getByRole('heading', { name: /Ayuda — Compras Requisiciones/i }),
    ).toBeInTheDocument();
    // Las 6 secciones h2.
    expect(
      screen.getByRole('heading', { name: /Estados de una requisición/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Ciclo de vida/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Naturalezas del artículo/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Matriz de autorización/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Conceptos del dominio/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Preguntas frecuentes/i }),
    ).toBeInTheDocument();
  });

  it('lista los 10 estados de RequisicionEstado', () => {
    render(<Ayuda />);
    const labels = [
      'Borrador',
      'En autorización',
      'Autorizada',
      'En surtido',
      'Cerrada',
      'Cancelada',
      'Rechazada',
      'Eliminada',
      'Cerrada sin surtir',
      'Cerrada surtida parcialmente',
    ];
    for (const l of labels) {
      // Cada estado aparece en un EstadoBadge.
      expect(screen.getAllByText(l).length).toBeGreaterThan(0);
    }
  });

  it('muestra link "Volver a la bandeja"', () => {
    render(<Ayuda />);
    const link = screen.getByRole('link', { name: /volver a la bandeja/i });
    expect(link).toHaveAttribute('href', '/compras/requisiciones');
  });

  it('FAQ tiene al menos 5 preguntas', () => {
    render(<Ayuda />);
    // Cada pregunta es un h3 dentro de su article.
    const articles = screen.getAllByRole('article');
    // Hay articles en estados (8), naturalezas (4), conceptos (4), faq (>=5).
    // Filtrar por articles que contienen "?" en su h3.
    const faqArticles = articles.filter(
      (a) => a.querySelector('h3')?.textContent?.includes('?') ?? false,
    );
    expect(faqArticles.length).toBeGreaterThanOrEqual(5);
  });
});

describe('<Ayuda> — accesibilidad (axe-core)', () => {
  it('cero violations WCAG 2.1 AA', async () => {
    const { container } = render(<Ayuda />);
    const results: AxeResults = await axe.run(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa'],
      },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map(
          (v) =>
            `${v.id} (${v.impact}): ${v.description}\n  Help: ${v.helpUrl}`,
        )
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
    expect(results.violations).toHaveLength(0);
  });
});
