import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DetalleErrorBoundary } from '@/features/compras/components/DetalleErrorBoundary';
import { ApiError } from '@/lib/api';

// Mock de Link de TanStack Router para evitar montar un router completo.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    to,
    children,
    className,
  }: {
    to: string;
    children: React.ReactNode;
    className?: string;
  }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
}));

afterEach(() => {
  vi.restoreAllMocks();
});

function buildError(status: number, code?: string, traceId?: string): ApiError {
  return new ApiError(
    {
      type: 'about:blank',
      title: `HTTP ${status}`,
      status,
      ...(code !== undefined ? { code } : {}),
      ...(traceId !== undefined ? { traceId } : {}),
    },
    status,
  );
}

describe('<DetalleErrorBoundary>', () => {
  it('403: muestra página de "sin acceso" con icono de candado y CTA volver a bandeja', () => {
    // Silenciar el console.warn que el componente emite.
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

    render(
      <DetalleErrorBoundary error={buildError(403, undefined, '00-trace-01')} />,
    );

    expect(
      screen.getByText(/no tienes permiso para ver esta requisición/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/00-trace-01/)).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: /volver a bandeja/i }),
    ).toHaveAttribute('href', '/compras/requisiciones');

    expect(warnSpy).toHaveBeenCalled();
  });

  it('404: muestra página neutra "no existe o no pertenece a tu empresa"', () => {
    render(<DetalleErrorBoundary error={buildError(404)} />);
    expect(
      screen.getByText(
        /esta requisición no existe o no pertenece a tu empresa/i,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: /volver a bandeja/i }),
    ).toHaveAttribute('href', '/compras/requisiciones');
  });

  it('5xx: cae a <ErrorState> con botón Reintentar', () => {
    const onRetry = vi.fn();
    render(<DetalleErrorBoundary error={buildError(500)} onRetry={onRetry} />);
    expect(
      screen.getByRole('button', { name: /reintentar/i }),
    ).toBeInTheDocument();
  });

  it('error no-API (network): cae a 5xx path', () => {
    render(<DetalleErrorBoundary error={new Error('Network failure')} />);
    expect(screen.getByText('Algo salió mal')).toBeInTheDocument();
  });
});
