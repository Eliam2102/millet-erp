import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';

describe('<AvisoEnLinea>', () => {
  it('advertencia usa role note', () => {
    render(<AvisoEnLinea tono="advertencia">Falta X</AvisoEnLinea>);
    expect(screen.getByRole('note')).toHaveTextContent('Falta X');
  });

  it('error usa role alert y renderiza titulo y accion', () => {
    render(
      <AvisoEnLinea tono="error" titulo="Bloqueado" accion={<button>Ir</button>}>
        Corrige la OC
      </AvisoEnLinea>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Bloqueado');
    expect(screen.getByRole('button', { name: 'Ir' })).toBeInTheDocument();
  });
});
