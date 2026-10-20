import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ConMotivo } from '@/features/compras/components/ConMotivo';

describe('<ConMotivo>', () => {
  it('con motivo envuelve el botón en un span enfocable', () => {
    render(
      <ConMotivo motivo="Falta una partida">
        <button disabled>Transmitir</button>
      </ConMotivo>,
    );
    expect(screen.getByRole('button', { name: 'Transmitir' }).parentElement).toHaveAttribute('tabindex', '0');
  });

  it('sin motivo no agrega envoltorio', () => {
    const { container } = render(
      <ConMotivo motivo={undefined}>
        <button>Transmitir</button>
      </ConMotivo>,
    );
    expect(container.firstElementChild?.tagName).toBe('BUTTON');
  });
});
