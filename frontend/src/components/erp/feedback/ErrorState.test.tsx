import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ErrorState } from '@/components/erp/feedback/ErrorState';

describe('<ErrorState>', () => {
  it('muestra title del problem cuando se pasa', () => {
    render(
      <ErrorState
        problem={{
          type: 'about:blank',
          title: 'Servicio no disponible',
          status: 503,
          detail: 'El backend no responde.',
          traceId: '00-abc-def-01',
        }}
      />,
    );

    expect(
      screen.getByText('Servicio no disponible'),
    ).toBeInTheDocument();
    expect(screen.getByText('El backend no responde.')).toBeInTheDocument();
    expect(screen.getByText(/00-abc-def-01/)).toBeInTheDocument();
  });

  it('cae a un mensaje genérico sin problem', () => {
    render(<ErrorState />);
    expect(screen.getByText('Algo salió mal')).toBeInTheDocument();
    expect(
      screen.getByText(/si el problema persiste/i),
    ).toBeInTheDocument();
  });

  it('renderiza el botón Reintentar e invoca onRetry al click', () => {
    const onRetry = vi.fn();
    render(<ErrorState onRetry={onRetry} />);

    fireEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('omite el botón Reintentar cuando NO se pasa onRetry', () => {
    render(<ErrorState />);
    expect(screen.queryByRole('button', { name: /reintentar/i })).not.toBeInTheDocument();
  });

  it('expone role="alert" para screen readers', () => {
    const { container } = render(<ErrorState />);
    expect(container.querySelector('[role="alert"]')).not.toBeNull();
  });
});
