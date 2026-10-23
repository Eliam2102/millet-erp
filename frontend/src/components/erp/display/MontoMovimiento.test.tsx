import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MontoMovimiento } from './MontoMovimiento';
describe('MontoMovimiento', () => {
  it.each([
    ['entrada', '+$12.00', 'text-success-fg'],
    ['salida', '−$12.00', 'text-danger-fg'],
  ] as const)('%s tiene signo y tono', (tipo, texto, clase) => {
    render(<MontoMovimiento tipo={tipo} monto={-12} moneda="MXN" />);
    expect(screen.getByText(texto)).toHaveClass(clase, 'tabular-nums');
  });
});
