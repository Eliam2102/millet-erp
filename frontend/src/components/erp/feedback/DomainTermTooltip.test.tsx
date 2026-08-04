import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DomainTermTooltip } from '@/components/erp/feedback/DomainTermTooltip';

describe('<DomainTermTooltip>', () => {
  it('renderiza solo children cuando definicion es undefined', () => {
    render(
      <DomainTermTooltip term="EnSurtido">
        <span>EnSurtido</span>
      </DomainTermTooltip>,
    );

    expect(screen.getByText('EnSurtido')).toBeInTheDocument();
    // Sin definicion, no envuelve en TooltipProvider/Tooltip — el span
    // es el primer hijo directo.
    expect(
      document.querySelector('[data-domain-term]'),
    ).toBeNull();
  });

  it('envuelve children con data-domain-term cuando hay definicion', () => {
    render(
      <DomainTermTooltip
        term="EnSurtido"
        definicion="La requisición está autorizada y esperando entrega."
      >
        <span>EnSurtido</span>
      </DomainTermTooltip>,
    );

    const wrapper = document.querySelector('[data-domain-term="EnSurtido"]');
    expect(wrapper).not.toBeNull();
    expect(wrapper?.textContent).toBe('EnSurtido');
  });
});
