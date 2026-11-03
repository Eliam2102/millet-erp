import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { HoverSubEstados } from './HoverSubEstados';
import { SubEstadoRecepcion, SubEstadoFacturacion, SubEstadoPago } from '../api/types';

describe('avance compacto de OC', () => {
  it('cada círculo explica su dimensión y estado por title y aria-label', () => {
    render(
      <HoverSubEstados
        recepcion={SubEstadoRecepcion.Parcial}
        facturacion={SubEstadoFacturacion.SinFactura}
        pago={SubEstadoPago.SinPago}
      />,
    );
    for (const letra of ['R', 'F', 'P']) {
      const chip = screen.getByText(letra);
      expect(chip).toHaveAttribute('aria-label', chip.getAttribute('title'));
      expect(chip.getAttribute('title')).toMatch(/^(Recepción|Facturación|Pago): .+/);
    }
  });
});
