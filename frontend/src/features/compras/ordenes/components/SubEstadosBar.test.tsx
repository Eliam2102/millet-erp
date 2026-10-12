import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import {
  SubEstadosBar,
  porcentajeLineas,
} from '@/features/compras/ordenes/components/SubEstadosBar';
import {
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
  subEstadoPagoToString,
} from '@/features/compras/ordenes/api/types';

describe('<SubEstadosBar>', () => {
  it('renderiza las 3 dimensiones (Recepción / Facturación / Pago)', () => {
    render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.SinRecepcion}
        facturacion={SubEstadoFacturacion.SinFactura}
        pago={SubEstadoPago.SinPago}
      />,
    );
    expect(screen.getByText('Recepción')).toBeInTheDocument();
    expect(screen.getByText('Facturación')).toBeInTheDocument();
    expect(screen.getByText('Pago')).toBeInTheDocument();
  });

  it('progreso=vacio cuando todos son Sin*', () => {
    const { container } = render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.SinRecepcion}
        facturacion={SubEstadoFacturacion.SinFactura}
        pago={SubEstadoPago.SinPago}
      />,
    );
    expect(
      container.querySelectorAll('[data-progreso="vacio"]').length,
    ).toBe(3);
  });

  it('progreso=parcial para Parcial en cualquier dimensión', () => {
    const { container } = render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.Parcial}
        facturacion={SubEstadoFacturacion.Parcial}
        pago={SubEstadoPago.Parcial}
      />,
    );
    expect(
      container.querySelectorAll('[data-progreso="parcial"]').length,
    ).toBe(3);
  });

  it('progreso=completo para Completa/Pagada', () => {
    const { container } = render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.Completa}
        facturacion={SubEstadoFacturacion.Completa}
        pago={SubEstadoPago.Pagada}
      />,
    );
    expect(
      container.querySelectorAll('[data-progreso="completo"]').length,
    ).toBe(3);
  });

  it('mezcla de progresos: refleja cada dimensión por separado', () => {
    const { container } = render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.Completa}
        facturacion={SubEstadoFacturacion.Parcial}
        pago={SubEstadoPago.SinPago}
      />,
    );
    expect(
      container
        .querySelector('[data-sub-estado="recepcion"]')
        ?.getAttribute('data-progreso'),
    ).toBe('completo');
    expect(
      container
        .querySelector('[data-sub-estado="facturacion"]')
        ?.getAttribute('data-progreso'),
    ).toBe('parcial');
    expect(
      container
        .querySelector('[data-sub-estado="pago"]')
        ?.getAttribute('data-progreso'),
    ).toBe('vacio');
  });

  it('renderiza label textual del sub-estado (no solo color) — accesibilidad', () => {
    render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.Parcial}
        facturacion={SubEstadoFacturacion.Completa}
        pago={SubEstadoPago.Pagada}
      />,
    );
    // Cada barra muestra el label legible del sub-estado además del color.
    expect(screen.getByText(/Recepción parcial/)).toBeInTheDocument();
    expect(screen.getByText(/Facturación completa/)).toBeInTheDocument();
    expect(screen.getByText(/Pagada/)).toBeInTheDocument();
  });

  it('expone progressbar accesible con aria-valuenow', () => {
    const { container } = render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.Completa}
        facturacion={SubEstadoFacturacion.Parcial}
        pago={SubEstadoPago.SinPago}
      />,
    );
    const bars = container.querySelectorAll('[role="progressbar"]');
    expect(bars).toHaveLength(3);
    const valores = Array.from(bars).map((b) => b.getAttribute('aria-valuenow'));
    // Parcial sin líneas: sin valor (indeterminado), no un 50 inventado.
    expect(valores).toEqual(['100', null, '0']);
  });

  it('container expone aria-label de grupo', () => {
    const { container } = render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.SinRecepcion}
        facturacion={SubEstadoFacturacion.SinFactura}
        pago={SubEstadoPago.SinPago}
      />,
    );
    const root = container.querySelector('[data-component="sub-estados-bar"]');
    expect(root?.getAttribute('role')).toBe('group');
    expect(root?.getAttribute('aria-label')).toMatch(/Sub-estados/i);
  });

  it('muestra el % real por cantidades (6/10 + 0/20 = 20 %) y no el literal ~50', () => {
    render(
      <SubEstadosBar
        recepcion={SubEstadoRecepcion.Parcial}
        facturacion={SubEstadoFacturacion.Parcial}
        pago={SubEstadoPago.Parcial}
        lineas={[
          { cantidad: 10, cantidadRecibida: 6, cantidadFacturada: 5 },
          { cantidad: 20, cantidadRecibida: 0, cantidadFacturada: 0 },
        ]}
      />,
    );
    expect(screen.getByText('Recepción parcial (20 %)')).toBeInTheDocument();
    expect(screen.getByText('Facturación parcial (17 %)')).toBeInTheDocument();
    // Pago: el DTO no expone avance -> solo estado, sin porcentaje inventado.
    expect(screen.getByText(subEstadoPagoToString(SubEstadoPago.Parcial))).toBeInTheDocument();
    expect(screen.queryByText(/~50/)).toBeNull();
  });

  it('porcentajeLineas excluye servicios en recepción y devuelve undefined sin líneas', () => {
    expect(porcentajeLineas(undefined, 'cantidadRecibida')).toBeUndefined();
    expect(
      porcentajeLineas(
        [
          { cantidad: 10, cantidadRecibida: 5, cantidadFacturada: 0 },
          { cantidad: 90, cantidadRecibida: 0, cantidadFacturada: 0, esServicio: true },
        ],
        'cantidadRecibida',
      ),
    ).toBe(50);
  });
});
