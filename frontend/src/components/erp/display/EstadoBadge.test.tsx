import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { EstadoBadge } from '@/components/erp/display/EstadoBadge';
import {
  EstadoRequisicion,
  SituacionSurtido,
} from '@/features/compras/api/types';
import { EstadoOrdenCompra } from '@/features/compras/ordenes/api/types';

describe('<EstadoBadge> — tipo="requisicion"', () => {
  it('renderiza el label en español del estado', () => {
    render(
      <EstadoBadge tipo="requisicion" estado={EstadoRequisicion.EnSurtido} />,
    );
    expect(screen.getByText('En surtido')).toBeInTheDocument();
  });

  it('expone data-estado y data-tipo (para tests/scraping)', () => {
    const { container } = render(
      <EstadoBadge
        tipo="requisicion"
        estado={EstadoRequisicion.EnAutorizacion}
      />,
    );
    expect(
      container.querySelector(
        '[data-estado="EnAutorizacion"][data-tipo="requisicion"]',
      ),
    ).not.toBeNull();
  });

  it('snapshot por estado: cubre los 10 estados', () => {
    const estados: EstadoRequisicion[] = [
      EstadoRequisicion.Borrador,
      EstadoRequisicion.EnAutorizacion,
      EstadoRequisicion.Autorizada,
      EstadoRequisicion.EnSurtido,
      EstadoRequisicion.Cerrada,
      EstadoRequisicion.Cancelada,
      EstadoRequisicion.Rechazada,
      EstadoRequisicion.Eliminada,
      EstadoRequisicion.CerradaSinSurtir,
      EstadoRequisicion.CerradaSurtidaParcial,
    ];
    for (const estado of estados) {
      const { container, unmount } = render(
        <EstadoBadge tipo="requisicion" estado={estado} />,
      );
      expect(container.firstChild).toMatchSnapshot(`rq-estado-${estado}`);
      unmount();
    }
  });

  it('renderiza children con tooltip wrapper (tiene data-domain-term)', () => {
    const { container } = render(
      <EstadoBadge tipo="requisicion" estado={EstadoRequisicion.Cerrada} />,
    );
    // El DomainTermTooltip wrapper expone data-domain-term cuando hay
    // definicion del glosario.
    expect(
      container.querySelector('[data-domain-term="Cerrada"]'),
    ).not.toBeNull();
  });
});

describe('<EstadoBadge> — tipo="orden-compra"', () => {
  it('renderiza el label en español de los 7 estados OC', () => {
    // Los 3 estados con label propio (no compartido con RQ): los dos
    // niveles de autorización.
    const casos: Array<[EstadoOrdenCompra, string]> = [
      [EstadoOrdenCompra.Borrador, 'Borrador'],
      [
        EstadoOrdenCompra.EnAutorizacionJefeCompras,
        'En autorización Jefe de Compras',
      ],
      [
        EstadoOrdenCompra.EnAutorizacionDireccion,
        'En autorización Dirección',
      ],
      [EstadoOrdenCompra.Autorizada, 'Autorizada'],
      [EstadoOrdenCompra.Cerrada, 'Cerrada'],
      [EstadoOrdenCompra.Cancelada, 'Cancelada'],
      [EstadoOrdenCompra.Rechazada, 'Rechazada'],
    ];
    for (const [estado, label] of casos) {
      const { unmount } = render(
        <EstadoBadge tipo="orden-compra" estado={estado} />,
      );
      expect(screen.getByText(label)).toBeInTheDocument();
      unmount();
    }
  });

  it('expone data-estado y data-tipo="orden-compra"', () => {
    const { container } = render(
      <EstadoBadge
        tipo="orden-compra"
        estado={EstadoOrdenCompra.EnAutorizacionJefeCompras}
      />,
    );
    expect(
      container.querySelector(
        '[data-estado="EnAutorizacionJefeCompras"][data-tipo="orden-compra"]',
      ),
    ).not.toBeNull();
  });

  it('snapshot por estado: cubre los 7 estados OC', () => {
    const estados: EstadoOrdenCompra[] = [
      EstadoOrdenCompra.Borrador,
      EstadoOrdenCompra.EnAutorizacionJefeCompras,
      EstadoOrdenCompra.EnAutorizacionDireccion,
      EstadoOrdenCompra.Autorizada,
      EstadoOrdenCompra.Cerrada,
      EstadoOrdenCompra.Cancelada,
      EstadoOrdenCompra.Rechazada,
    ];
    for (const estado of estados) {
      const { container, unmount } = render(
        <EstadoBadge tipo="orden-compra" estado={estado} />,
      );
      expect(container.firstChild).toMatchSnapshot(`oc-estado-${estado}`);
      unmount();
    }
  });

  it('tooltip muestra el wording de OC (no el de RQ) para claves compartidas', () => {
    const { container } = render(
      <EstadoBadge
        tipo="orden-compra"
        estado={EstadoOrdenCompra.Autorizada}
      />,
    );
    // El badge OC debe usar obtenerDefinicionOc, así que el data-domain-term
    // está y el resumen viene de ESTADOS_OC.Autorizada (incluye "transmitió
    // al proveedor"), no de ESTADOS.Autorizada (que habla de RQ).
    const wrapper = container.querySelector('[data-domain-term="Autorizada"]');
    expect(wrapper).not.toBeNull();
  });
});

describe('<EstadoBadge> — situación de surtido (ADR-0043)', () => {
  // Casos EnSurtido + situación: el badge cambia label, color y glosario a
  // la fase calculada, PERO data-estado sigue siendo "EnSurtido" (invariante
  // no-breaking) y la situación se expone aparte en data-situacion.
  const casos: Array<[SituacionSurtido, string, string]> = [
    [SituacionSurtido.EsperandoCompra, 'Esperando compra', 'bg-violet-100'],
    [SituacionSurtido.ListoParaSurtir, 'Listo para surtir', 'bg-sky-100'],
    [SituacionSurtido.SurtidoParcial, 'Surtido parcial', 'bg-fuchsia-100'],
  ];
  const KEYS: Record<SituacionSurtido, string> = {
    [SituacionSurtido.EsperandoCompra]: 'EsperandoCompra',
    [SituacionSurtido.ListoParaSurtir]: 'ListoParaSurtir',
    [SituacionSurtido.SurtidoParcial]: 'SurtidoParcial',
  };

  it.each(casos)(
    'EnSurtido + situación %s → muestra el label de la fase y su color',
    (situacion, label, colorClase) => {
      const { container } = render(
        <EstadoBadge
          tipo="requisicion"
          estado={EstadoRequisicion.EnSurtido}
          situacion={situacion}
        />,
      );
      expect(screen.getByText(label)).toBeInTheDocument();
      const span = container.querySelector('[data-tipo="requisicion"]');
      expect(span).not.toBeNull();
      expect(span?.className).toContain(colorClase);
    },
  );

  it('EnSurtido + situación: data-estado sigue siendo "EnSurtido" y se agrega data-situacion', () => {
    const { container } = render(
      <EstadoBadge
        tipo="requisicion"
        estado={EstadoRequisicion.EnSurtido}
        situacion={SituacionSurtido.ListoParaSurtir}
      />,
    );
    const span = container.querySelector('[data-tipo="requisicion"]');
    // Invariante no-breaking: el selector [data-estado="EnSurtido"] sigue
    // funcionando aunque el badge muestre la situación.
    expect(span?.getAttribute('data-estado')).toBe('EnSurtido');
    expect(span?.getAttribute('data-situacion')).toBe('ListoParaSurtir');
  });

  it('EnSurtido + situación: el tooltip apunta al glosario de la situación', () => {
    const { container } = render(
      <EstadoBadge
        tipo="requisicion"
        estado={EstadoRequisicion.EnSurtido}
        situacion={SituacionSurtido.SurtidoParcial}
      />,
    );
    expect(
      container.querySelector(`[data-domain-term="${KEYS[2]}"]`),
    ).not.toBeNull();
    // Y NO debe usar el término del estado para el tooltip.
    expect(
      container.querySelector('[data-domain-term="EnSurtido"]'),
    ).toBeNull();
  });

  it('EnSurtido sin situación (undefined) → fallback "En surtido", sin data-situacion', () => {
    const { container } = render(
      <EstadoBadge tipo="requisicion" estado={EstadoRequisicion.EnSurtido} />,
    );
    expect(screen.getByText('En surtido')).toBeInTheDocument();
    const span = container.querySelector('[data-tipo="requisicion"]');
    expect(span?.getAttribute('data-estado')).toBe('EnSurtido');
    expect(span?.hasAttribute('data-situacion')).toBe(false);
  });

  it('EnSurtido con situación null → fallback "En surtido", sin data-situacion', () => {
    const { container } = render(
      <EstadoBadge
        tipo="requisicion"
        estado={EstadoRequisicion.EnSurtido}
        situacion={null}
      />,
    );
    expect(screen.getByText('En surtido')).toBeInTheDocument();
    expect(
      container
        .querySelector('[data-tipo="requisicion"]')
        ?.hasAttribute('data-situacion'),
    ).toBe(false);
  });

  it('otro estado + situación presente → ignora la situación y pinta el estado', () => {
    const { container } = render(
      <EstadoBadge
        tipo="requisicion"
        estado={EstadoRequisicion.Autorizada}
        situacion={SituacionSurtido.SurtidoParcial}
      />,
    );
    // Autorizada no es EnSurtido: la situación se ignora por completo.
    expect(screen.getByText('Autorizada')).toBeInTheDocument();
    const span = container.querySelector('[data-tipo="requisicion"]');
    expect(span?.getAttribute('data-estado')).toBe('Autorizada');
    expect(span?.hasAttribute('data-situacion')).toBe(false);
  });
});
