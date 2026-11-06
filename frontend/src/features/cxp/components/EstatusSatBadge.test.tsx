import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { EstatusSatBadge } from '@/features/cxp/components/EstatusSatBadge';
import { EstatusSatCfdi } from '@/features/cxp/api/types';

function textoSinBasura(texto: string | null) {
  expect(texto).not.toMatch(/undefined|null|NaN/);
}

describe('<EstatusSatBadge>', () => {
  it('Vigente muestra "Vigente"', () => {
    const { container } = render(<EstatusSatBadge estatus={EstatusSatCfdi.Vigente} />);
    expect(container).toHaveTextContent('Vigente');
    expect(container.querySelector('[data-slot="badge"]')).toHaveClass('bg-success-bg');
    textoSinBasura(container.textContent);
  });

  it('Cancelado muestra la etiqueta y el día/mes de la cancelación', () => {
    const { container } = render(
      <EstatusSatBadge
        estatus={EstatusSatCfdi.Cancelado}
        fechaCancelacion="2026-11-02T12:00:00Z"
      />,
    );
    expect(container).toHaveTextContent('Cancelado');
    expect(container.querySelector('[data-slot="badge"]')).toHaveClass('bg-danger-bg', 'text-danger-fg');
    // 12:00Z = 06:00 en México: sigue siendo el 2 de noviembre.
    expect(container).toHaveTextContent(/Cancelado\s*·\s*2 nov/);
    // Espacio real tras la etiqueta: el separador va aria-hidden.
    expect(container.textContent).toMatch(/Cancelado\s+·/);
    textoSinBasura(container.textContent);
  });

  it.each([null, undefined])('estatus %s muestra «—»', (estatus) => {
    const { container } = render(<EstatusSatBadge estatus={estatus} />);
    expect(container).toHaveTextContent('—');
    textoSinBasura(container.textContent);
  });
});
