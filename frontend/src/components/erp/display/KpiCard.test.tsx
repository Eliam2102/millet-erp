import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { KpiCard } from './KpiCard';
describe('KpiCard', () => {
  it('enlaza al listado y muestra valor, subdato y prioridad', () => {
    render(
      <KpiCard
        etiqueta="Pendientes"
        valor={12}
        subdato="Esta semana"
        tono="danger"
        prioridad="critico"
        href="/facturas"
      />,
    );
    expect(screen.getByRole('link')).toHaveAttribute('href', '/facturas');
    expect(screen.getByRole('link')).toHaveClass('ring-danger-ring');
    expect(screen.getByText('12')).toHaveClass('tabular-nums');
    expect(screen.getByText('Crítico')).toHaveClass('bg-danger-bg');
    expect(screen.getByText('Esta semana')).toBeInTheDocument();
  });
  it('admite indicador sin enlace y atención', () => {
    render(<KpiCard etiqueta="Pendientes" valor={0} prioridad="atencion" />);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.getByText('Atención')).toHaveClass('bg-warning-bg');
  });
});
