import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StepperAutorizacionOc } from '@/features/compras/ordenes/components/StepperAutorizacionOc';
import { EstadoOrdenCompra } from '@/features/compras/ordenes/api/types';

function leerPasos(container: HTMLElement) {
  const items = Array.from(container.querySelectorAll('[data-paso]'));
  return items.map((el) => ({
    id: el.getAttribute('data-paso'),
    estado: el.getAttribute('data-estado-paso'),
    ariaCurrent: el.getAttribute('aria-current'),
  }));
}

describe('<StepperAutorizacionOc>', () => {
  it('renderiza los 4 pasos: Borrador → Jefe Compras → Dirección → Autorizada', () => {
    render(<StepperAutorizacionOc estado={EstadoOrdenCompra.Borrador} />);
    expect(screen.getByText('Borrador')).toBeInTheDocument();
    expect(screen.getByText(/Jefe Compras/)).toBeInTheDocument();
    expect(screen.getByText(/Dirección/)).toBeInTheDocument();
    expect(screen.getByText('Autorizada')).toBeInTheDocument();
  });

  it('Borrador: Borrador=actual, resto pendiente', () => {
    const { container } = render(
      <StepperAutorizacionOc estado={EstadoOrdenCompra.Borrador} />,
    );
    expect(leerPasos(container)).toEqual([
      { id: 'borrador', estado: 'actual', ariaCurrent: 'step' },
      { id: 'n1', estado: 'pendiente', ariaCurrent: null },
      { id: 'n2', estado: 'pendiente', ariaCurrent: null },
      { id: 'autorizada', estado: 'pendiente', ariaCurrent: null },
    ]);
  });

  it('EnAutorizacionJefeCompras: Borrador=completo, N1=actual, resto pendiente', () => {
    const { container } = render(
      <StepperAutorizacionOc
        estado={EstadoOrdenCompra.EnAutorizacionJefeCompras}
      />,
    );
    expect(leerPasos(container)).toEqual([
      { id: 'borrador', estado: 'completo', ariaCurrent: null },
      { id: 'n1', estado: 'actual', ariaCurrent: 'step' },
      { id: 'n2', estado: 'pendiente', ariaCurrent: null },
      { id: 'autorizada', estado: 'pendiente', ariaCurrent: null },
    ]);
  });

  it('EnAutorizacionDireccion: Borrador y N1 completos, N2=actual, Autorizada pendiente', () => {
    const { container } = render(
      <StepperAutorizacionOc
        estado={EstadoOrdenCompra.EnAutorizacionDireccion}
      />,
    );
    expect(leerPasos(container)).toEqual([
      { id: 'borrador', estado: 'completo', ariaCurrent: null },
      { id: 'n1', estado: 'completo', ariaCurrent: null },
      { id: 'n2', estado: 'actual', ariaCurrent: 'step' },
      { id: 'autorizada', estado: 'pendiente', ariaCurrent: null },
    ]);
  });

  it('Autorizada: los 4 pasos completos', () => {
    const { container } = render(
      <StepperAutorizacionOc estado={EstadoOrdenCompra.Autorizada} />,
    );
    const estados = leerPasos(container).map((p) => p.estado);
    expect(estados).toEqual(['completo', 'completo', 'completo', 'completo']);
    // No hay paso "actual" — todos completos.
    expect(
      container.querySelector('[aria-current="step"]'),
    ).toBeNull();
  });

  it('Cerrada: igual que Autorizada (los 4 completos)', () => {
    const { container } = render(
      <StepperAutorizacionOc estado={EstadoOrdenCompra.Cerrada} />,
    );
    const estados = leerPasos(container).map((p) => p.estado);
    expect(estados).toEqual(['completo', 'completo', 'completo', 'completo']);
  });

  it('Rechazada: marca N1 y N2 como rechazado, Autorizada pendiente', () => {
    const { container } = render(
      <StepperAutorizacionOc estado={EstadoOrdenCompra.Rechazada} />,
    );
    expect(leerPasos(container)).toEqual([
      { id: 'borrador', estado: 'completo', ariaCurrent: null },
      { id: 'n1', estado: 'rechazado', ariaCurrent: null },
      { id: 'n2', estado: 'rechazado', ariaCurrent: null },
      { id: 'autorizada', estado: 'pendiente', ariaCurrent: null },
    ]);
  });

  it('Cancelada: Borrador=completo, resto pendiente (header da el contexto Cancelada)', () => {
    const { container } = render(
      <StepperAutorizacionOc estado={EstadoOrdenCompra.Cancelada} />,
    );
    expect(leerPasos(container)).toEqual([
      { id: 'borrador', estado: 'completo', ariaCurrent: null },
      { id: 'n1', estado: 'pendiente', ariaCurrent: null },
      { id: 'n2', estado: 'pendiente', ariaCurrent: null },
      { id: 'autorizada', estado: 'pendiente', ariaCurrent: null },
    ]);
  });

  it('aria-label del contenedor describe el flujo', () => {
    const { container } = render(
      <StepperAutorizacionOc estado={EstadoOrdenCompra.Borrador} />,
    );
    const root = container.querySelector(
      '[data-component="stepper-autorizacion-oc"]',
    );
    expect(root?.getAttribute('aria-label')).toMatch(/autorización/i);
  });
});
