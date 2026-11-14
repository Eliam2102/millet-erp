import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { VariacionBadge } from './VariacionBadge';

describe('VariacionBadge', () => {
  it('con existencia teórica 0 (sin porcentaje) muestra — y no 0.00 %', () => {
    render(
      <VariacionBadge
        variacionAbsoluta={1}
        variacionPorcentaje={null}
        variacionValorMxn={50}
        requiereRecuento={false}
      />,
    );
    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.getByText('sin porcentaje, existencia teórica en cero')).toBeInTheDocument();
    expect(screen.queryByText(/0\.00 %/)).not.toBeInTheDocument();
  });

  it('con porcentaje lo muestra con dos decimales', () => {
    render(
      <VariacionBadge
        variacionAbsoluta={-2}
        variacionPorcentaje={-12.5}
        variacionValorMxn={-100}
        requiereRecuento={false}
      />,
    );
    expect(screen.getByText(/\(-12\.50 %\)/)).toBeInTheDocument();
  });
});
