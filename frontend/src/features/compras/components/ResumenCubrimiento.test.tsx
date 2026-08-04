import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ResumenCubrimiento } from '@/features/compras/components/ResumenCubrimiento';
import {
  EstadoRequisicion,
  type LineaResponse,
} from '@/features/compras/api/types';

function linea(cantidad: number, cantPendiente: number): LineaResponse {
  return {
    id: `l-${cantidad}-${cantPendiente}`,
    posicion: 1,
    articuloId: 'a-1',
    cantidad,
    unidadMedida: 'PZA',
    precioEstimadoMonto: 100,
    precioEstimadoMoneda: 'MXN',
    cuentaContableId: null,
    centroCostoId: null,
    proyecto: null,
    fechaRequerida: null,
    notas: null,
    cantDeAlmacen: cantidad - cantPendiente,
    cantDeCompra: 0,
    cantRecibida: 0,
    cantPendiente,
    reservaId: null,
  };
}

describe('<ResumenCubrimiento>', () => {
  it('estado pre-aut (Borrador): no se muestra', () => {
    const { container } = render(
      <ResumenCubrimiento
        lineas={[linea(10, 0)]}
        estado={EstadoRequisicion.Borrador}
      />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('estado terminal (Cancelada): no se muestra', () => {
    const { container } = render(
      <ResumenCubrimiento
        lineas={[linea(10, 0)]}
        estado={EstadoRequisicion.Cancelada}
      />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('estado Autorizada con 0 líneas: no se muestra', () => {
    const { container } = render(
      <ResumenCubrimiento
        lineas={[]}
        estado={EstadoRequisicion.Autorizada}
      />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('Autorizada con 3/5 cerradas: muestra 60% (3 de 5 líneas cerradas)', () => {
    render(
      <ResumenCubrimiento
        lineas={[
          linea(10, 0),
          linea(10, 0),
          linea(10, 0),
          linea(10, 5),
          linea(10, 8),
        ]}
        estado={EstadoRequisicion.Autorizada}
      />,
    );
    expect(screen.getByText('60%')).toBeInTheDocument();
    expect(screen.getByText(/3 de 5 líneas cerradas/i)).toBeInTheDocument();
  });

  it('EnSurtido con todas cerradas: 100% (badge verde)', () => {
    const { container } = render(
      <ResumenCubrimiento
        lineas={[linea(5, 0), linea(3, 0)]}
        estado={EstadoRequisicion.EnSurtido}
      />,
    );
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(screen.getByText(/2 de 2 líneas cerradas/i)).toBeInTheDocument();
    // Verde: tiene clase emerald.
    expect(container.querySelector('[class*="emerald"]')).not.toBeNull();
  });

  it('EnSurtido con 0 cerradas: 0% (badge ámbar)', () => {
    const { container } = render(
      <ResumenCubrimiento
        lineas={[linea(10, 5), linea(10, 3)]}
        estado={EstadoRequisicion.EnSurtido}
      />,
    );
    expect(screen.getByText('0%')).toBeInTheDocument();
    expect(container.querySelector('[class*="amber"]')).not.toBeNull();
  });

  it('aria-label refleja la información completa', () => {
    render(
      <ResumenCubrimiento
        lineas={[linea(10, 0), linea(10, 5)]}
        estado={EstadoRequisicion.Autorizada}
      />,
    );
    const status = screen.getByRole('status');
    expect(status).toHaveAttribute(
      'aria-label',
      expect.stringMatching(/50%.*1 de 2 líneas/i),
    );
  });

  it('singular: 1 de 1 línea cerrada', () => {
    render(
      <ResumenCubrimiento
        lineas={[linea(10, 0)]}
        estado={EstadoRequisicion.Autorizada}
      />,
    );
    expect(screen.getByText(/1 de 1 línea cerrada/i)).toBeInTheDocument();
  });
});
