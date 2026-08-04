import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { NaturalezaBadge } from '@/components/erp/display/NaturalezaBadge';
import { Naturaleza } from '@/features/compras/api/types';

describe('<NaturalezaBadge>', () => {
  it('renderiza el label con tilde correcto', () => {
    render(<NaturalezaBadge naturaleza={Naturaleza.Critico} />);
    expect(screen.getByText('Crítico')).toBeInTheDocument();
  });

  it('Estandar muestra "Estándar" (con tilde)', () => {
    render(<NaturalezaBadge naturaleza={Naturaleza.Estandar} />);
    expect(screen.getByText('Estándar')).toBeInTheDocument();
  });

  it('expone data-naturaleza con la key del enum sin tilde', () => {
    const { container } = render(
      <NaturalezaBadge naturaleza={Naturaleza.Critico} />,
    );
    expect(
      container.querySelector('[data-naturaleza="Critico"]'),
    ).not.toBeNull();
  });

  it('snapshot por naturaleza: cubre las 4', () => {
    const todas: Naturaleza[] = [
      Naturaleza.Estandar,
      Naturaleza.Servicio,
      Naturaleza.Critico,
      Naturaleza.Riesgo,
    ];
    for (const n of todas) {
      const { container, unmount } = render(<NaturalezaBadge naturaleza={n} />);
      expect(container.firstChild).toMatchSnapshot(`naturaleza-${n}`);
      unmount();
    }
  });
});
