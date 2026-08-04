import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Inbox } from 'lucide-react';
import { EmptyState } from '@/components/erp/feedback/EmptyState';

describe('<EmptyState>', () => {
  it('renderiza title y description', () => {
    render(
      <EmptyState
        title="Aún no tienes requisiciones."
        description="Crea la primera para empezar."
      />,
    );

    // role="status" en el wrapper para que screen readers anuncien el
    // cambio cuando una bandeja vacía aparece tras un fetch.
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(
      screen.getByText('Aún no tienes requisiciones.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Crea la primera para empezar.'),
    ).toBeInTheDocument();
  });

  it('renderiza icon cuando se provee (aria-hidden)', () => {
    const { container } = render(
      <EmptyState
        icon={<Inbox data-testid="icon" />}
        title="Vacío"
      />,
    );

    expect(screen.getByTestId('icon')).toBeInTheDocument();
    // El wrapper del icono es aria-hidden para que el screen reader no
    // duplique el título.
    expect(container.querySelector('[aria-hidden="true"]')).not.toBeNull();
  });

  it('renderiza el action cuando se provee', () => {
    render(
      <EmptyState
        title="Vacío"
        action={<button>Nueva RQ</button>}
      />,
    );

    expect(
      screen.getByRole('button', { name: /nueva rq/i }),
    ).toBeInTheDocument();
  });

  it('omite el action cuando NO se provee', () => {
    render(<EmptyState title="Vacío" />);
    // No hay botones en el render mínimo.
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
