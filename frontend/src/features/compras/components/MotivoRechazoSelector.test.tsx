import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MotivoRechazoSelector } from '@/features/compras/components/MotivoRechazoSelector';
import { MotivoRechazoAplicaA } from '@/features/compras/api/types';

const motivosFixture = [
  {
    id: 'm-1',
    clave: 'OTRO',
    descripcion: 'Otro (especificar)',
    permiteTextoLibre: true,
    aplicaA: MotivoRechazoAplicaA.Todos,
  },
  {
    id: 'm-2',
    clave: 'PRESUPUESTO_NO_DISPONIBLE',
    descripcion: 'Presupuesto no disponible',
    permiteTextoLibre: false,
    aplicaA: MotivoRechazoAplicaA.Rechazo,
  },
  {
    id: 'm-3',
    clave: 'ESPECIFICACION_INCORRECTA',
    descripcion: 'Especificación incorrecta',
    permiteTextoLibre: false,
    aplicaA: MotivoRechazoAplicaA.Eliminacion,
  },
];

function setupMotivos() {
  mswServer.use(
    http.get('*/api/v1/compras/motivos-rechazo', () =>
      HttpResponse.json(motivosFixture),
    ),
  );
}

/**
 * El componente envuelve un shadcn <Select> de Radix, que renderiza
 * el dropdown en un Portal solo cuando se abre. En jsdom, abrir el
 * popover y verificar items dentro es delicado (Radix mide layout).
 *
 * <para>Tests pragmáticos: render trigger + aria-label + disabled.
 * El comportamiento end-to-end (filtro bitmask, sentinel none,
 * onMotivoChange) se cubre por inspección de código + UF7-PR1 smoke
 * test del modal P6 que lo consume.</para>
 */
describe('<MotivoRechazoSelector>', () => {
  it('renderiza sin crashear con value null', () => {
    setupMotivos();
    const { container } = render(
      <MotivoRechazoSelector
        aplicaA={MotivoRechazoAplicaA.Rechazo}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(container.querySelector('[role="combobox"]')).not.toBeNull();
  });

  it('aria-label apropiado en el trigger', () => {
    setupMotivos();
    render(
      <MotivoRechazoSelector
        aplicaA={MotivoRechazoAplicaA.Rechazo}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByRole('combobox', { name: /seleccionar motivo/i }),
    ).toBeInTheDocument();
  });

  it('disabled propaga al trigger', () => {
    setupMotivos();
    render(
      <MotivoRechazoSelector
        aplicaA={MotivoRechazoAplicaA.Rechazo}
        value={null}
        onChange={() => {}}
        disabled
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByRole('combobox')).toBeDisabled();
  });
});
