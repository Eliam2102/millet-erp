import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { CubrimientoBar, type CubrimientoBarProps } from '@/components/erp/display/CubrimientoBar';

/**
 * Tests parametrizados de los 5 escenarios principales (doc 05 §11.3 +
 * F6 Rev. 3): solo almacén, solo compra, mixto, recibido parcial,
 * cerrado. Verifican aria-label, números visibles, y segmentos
 * renderizados (data-segmento).
 */

interface Caso {
  nombre: string;
  props: CubrimientoBarProps;
  /** Substring esperado en aria-label (numérico legible). */
  ariaIncluye: string[];
  /** Substring esperado en el texto inline (no depende de hover). */
  inlineIncluye: string[];
  /** Segmentos visibles (data-segmento). */
  segmentosVisibles: string[];
}

const CASOS: Caso[] = [
  {
    nombre: 'solo almacén (cubierta sin OC)',
    props: {
      cantidad: 10,
      cantDeAlmacen: 10,
      cantDeCompra: 0,
      cantRecibida: 0,
      cantPendiente: 0,
    },
    ariaIncluye: ['Cubrimiento de 10', '10 en almacén', 'línea cubierta'],
    inlineIncluye: ['10 total', '10 alm'],
    segmentosVisibles: ['almacen'],
  },
  {
    nombre: 'solo compra (todo en OC, ninguna recibida)',
    props: {
      cantidad: 10,
      cantDeAlmacen: 0,
      cantDeCompra: 10,
      cantRecibida: 0,
      cantPendiente: 0,
    },
    // Nota: "10 pendiente de recepción" significa que la OC ya cubre
    // la cantidad pero el material aún no llega; NO es "línea cubierta"
    // (ese flag se reserva para cuando todo está recibido o en almacén).
    ariaIncluye: [
      'Cubrimiento de 10',
      '10 pendiente de recepción',
    ],
    inlineIncluye: ['10 total', '10 OC'],
    segmentosVisibles: ['pendienteRecepcion'],
  },
  {
    nombre: 'mixto (almacén + compra + pendiente)',
    props: {
      cantidad: 10,
      cantDeAlmacen: 3,
      cantDeCompra: 5,
      cantRecibida: 0,
      cantPendiente: 2,
    },
    ariaIncluye: [
      '3 en almacén',
      '5 pendiente de recepción',
      '2 pendiente de compra',
    ],
    inlineIncluye: ['10 total', '3 alm', '5 OC', '2 pend'],
    segmentosVisibles: ['almacen', 'pendienteRecepcion', 'pendienteCompra'],
  },
  {
    nombre: 'recibido parcial (3 alm + 5 OC con 2 recibidas + 2 pend)',
    props: {
      cantidad: 10,
      cantDeAlmacen: 3,
      cantDeCompra: 5,
      cantRecibida: 2,
      cantPendiente: 2,
    },
    ariaIncluye: [
      '3 en almacén',
      '2 recibido',
      '3 pendiente de recepción',
      '2 pendiente de compra',
    ],
    inlineIncluye: ['10 total', '3 alm', '5 OC (2 rec)', '2 pend'],
    segmentosVisibles: [
      'almacen',
      'recibida',
      'pendienteRecepcion',
      'pendienteCompra',
    ],
  },
  {
    nombre: 'cerrada (todo recibido + sin pendiente)',
    props: {
      cantidad: 10,
      cantDeAlmacen: 0,
      cantDeCompra: 10,
      cantRecibida: 10,
      cantPendiente: 0,
    },
    ariaIncluye: ['Cubrimiento de 10', '10 recibido', 'línea cubierta'],
    inlineIncluye: ['10 total', '10 OC (10 rec)'],
    segmentosVisibles: ['recibida'],
  },
];

describe('<CubrimientoBar> — escenarios parametrizados', () => {
  it.each(CASOS)('$nombre', ({ props, ariaIncluye, inlineIncluye, segmentosVisibles }) => {
    const { container } = render(<CubrimientoBar {...props} />);

    // aria-label numérico (legible para screen readers).
    const ariaEl = screen.getByRole('img');
    const ariaLabel = ariaEl.getAttribute('aria-label') ?? '';
    for (const trozo of ariaIncluye) {
      expect(ariaLabel).toContain(trozo);
    }

    // Números siempre visibles al lado de la barra (R2 F6 Rev. 3).
    const inlineText = container.textContent ?? '';
    for (const trozo of inlineIncluye) {
      expect(inlineText).toContain(trozo);
    }

    // Segmentos con pct > 0 deben renderizarse; el resto no.
    const renderizados = container.querySelectorAll('[data-segmento]');
    const ids = Array.from(renderizados).map((n) => n.getAttribute('data-segmento'));
    expect(ids.sort()).toEqual([...segmentosVisibles].sort());
  });
});

describe('<CubrimientoBar> — bordes', () => {
  it('cantidad=0 no rompe (división segura)', () => {
    expect(() =>
      render(
        <CubrimientoBar
          cantidad={0}
          cantDeAlmacen={0}
          cantDeCompra={0}
          cantRecibida={0}
          cantPendiente={0}
        />,
      ),
    ).not.toThrow();
  });

  it('decimales se formatean a 2 sin ruido en enteros', () => {
    render(
      <CubrimientoBar
        cantidad={1.5}
        cantDeAlmacen={0.5}
        cantDeCompra={1}
        cantRecibida={0}
        cantPendiente={0}
      />,
    );
    expect(screen.getByText(/1\.50 total/)).toBeInTheDocument();
    expect(screen.getByText(/0\.50 alm/)).toBeInTheDocument();
  });
});

describe('<CubrimientoBar> — accesibilidad (axe-core)', () => {
  it('escenario mixto: cero issues axe-core (WCAG 2.1 AA)', async () => {
    const { container } = render(
      <CubrimientoBar
        cantidad={10}
        cantDeAlmacen={3}
        cantDeCompra={5}
        cantRecibida={2}
        cantPendiente={2}
      />,
    );

    const results: AxeResults = await axe.run(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa'],
      },
    });

    if (results.violations.length > 0) {
      // Mensaje legible si falla — útil para diagnóstico.
      const detalle = results.violations
        .map(
          (v) =>
            `${v.id} (${v.impact}): ${v.description}\n  Help: ${v.helpUrl}`,
        )
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }

    expect(results.violations).toHaveLength(0);
  });
});
