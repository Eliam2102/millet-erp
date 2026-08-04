import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { NivelPendienteBadge } from '@/components/erp/display/NivelPendienteBadge';
import { NivelAutorizacion } from '@/features/compras/api/types';

describe('<NivelPendienteBadge>', () => {
  it('Nivel1 → "Falta N1" con data-nivel-pendiente=1', () => {
    const { container } = render(
      <NivelPendienteBadge nivel={NivelAutorizacion.Nivel1} />,
    );
    expect(screen.getByText('Falta N1')).toBeInTheDocument();
    expect(
      container.querySelector('[data-nivel-pendiente="1"]'),
    ).not.toBeNull();
  });

  it('Nivel2 → "Falta N2" con data-nivel-pendiente=2', () => {
    const { container } = render(
      <NivelPendienteBadge nivel={NivelAutorizacion.Nivel2} />,
    );
    expect(screen.getByText('Falta N2')).toBeInTheDocument();
    expect(
      container.querySelector('[data-nivel-pendiente="2"]'),
    ).not.toBeNull();
  });

  it('es informativo (no es un botón)', () => {
    render(<NivelPendienteBadge nivel={NivelAutorizacion.Nivel1} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
