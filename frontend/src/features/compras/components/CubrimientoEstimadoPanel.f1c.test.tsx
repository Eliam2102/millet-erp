import { render, screen } from '@testing-library/react';
import { it, expect } from 'vitest';
import { CubrimientoEstimadoPanel } from './CubrimientoEstimadoPanel';
it('19-oct #10 muestra 12 disponibles y 18 a comprar para 30 solicitados, sin reinterpretar el DTO', () => {
  render(<CubrimientoEstimadoPanel lineas={[{ lineaId: 'l1', articuloId: 'a1', articuloClave: 'PRUEBA', articuloNombre: 'Sellador', cantidad: 30, disponible: 12, estimadoDeAlmacen: 12, estimadoDeCompra: 18 }]} />);
  expect(screen.getByRole('img')).toHaveAttribute('aria-label', 'Estimado: 12 disponible, 18 a comprar, de 30');
  expect(screen.getByText('12 disp · 18 a comprar')).toBeInTheDocument();
});
