import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Banknote } from 'lucide-react';
import { BandejaPlaceholder } from './BandejaPlaceholder';
import {
  ESTADO_APLICACION_LABELS,
  ESTADO_CORRIDA_LABELS,
  EstadoAplicacionMovimiento,
  EstadoCorridaPago,
  SentidoMovimiento,
} from '../api/types';

describe('BandejaPlaceholder (TES-FE-PR1)', () => {
  it('renderiza título, mensaje de arranque y chip de próximamente', () => {
    render(
      <BandejaPlaceholder
        titulo="Pagos a proveedor"
        descripcion="Pasivos autorizados por CxP."
        icono={Banknote}
        mensajeArranque="Los pasivos aparecen cuando CxP los autoriza."
        proximamente="Bandeja completa en TES-FE-PR2"
      />,
    );

    expect(
      screen.getByRole('heading', { name: /Pagos a proveedor/ }),
    ).toBeInTheDocument();
    expect(screen.getByTestId('tesoreria-bandeja-placeholder')).toHaveTextContent(
      'Los pasivos aparecen cuando CxP los autoriza.',
    );
    expect(screen.getByText('Bandeja completa en TES-FE-PR2')).toBeInTheDocument();
  });
});

describe('mirrors de enums (TES-FE-PR1)', () => {
  it('los valores espejo coinciden con los check constraints del backend', () => {
    // ck_movimiento_bancario_sentido: IN (1, 2)
    expect(SentidoMovimiento.Ingreso).toBe(1);
    expect(SentidoMovimiento.Egreso).toBe(2);
    // ck_movimiento_bancario_estado_aplicacion: IN (1, 2, 3)
    expect(Object.values(EstadoAplicacionMovimiento)).toEqual([1, 2, 3]);
    // ck_corrida_pago_estado: IN (1..7)
    expect(Object.values(EstadoCorridaPago)).toEqual([1, 2, 3, 4, 5, 6, 7]);
  });

  it('todo valor de enum tiene label', () => {
    for (const v of Object.values(EstadoAplicacionMovimiento)) {
      expect(ESTADO_APLICACION_LABELS[v]).toBeTruthy();
    }
    for (const v of Object.values(EstadoCorridaPago)) {
      expect(ESTADO_CORRIDA_LABELS[v]).toBeTruthy();
    }
  });
});
