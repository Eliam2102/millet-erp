import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { LineaDesdeRqBadge } from './LineaDesdeRqBadge';

/**
 * Fix A: con el folio resuelto por el backend, el badge muestra el folio
 * humano de la RQ y NO el GUID truncado. Sin folio (línea manual o RQ no
 * resuelta), cae al short-id del GUID — fallback histórico.
 */
describe('<LineaDesdeRqBadge>', () => {
  const RQ_ID = '019e937e-69ec-7630-bd90-5dbbf5218ad3';

  function renderBadge(folio?: string | null) {
    const { container } = render(
      <LineaDesdeRqBadge requisicionId={RQ_ID} folio={folio} />,
    );
    return container.querySelector(
      '[data-component="linea-desde-rq-badge"]',
    ) as HTMLElement;
  }

  it('con folio: muestra el folio de la RQ, no el GUID', () => {
    const badge = renderBadge('MID2026-000123');

    expect(badge.textContent).toContain('RQ MID2026-000123');
    // No debe filtrar el GUID en la etiqueta visible.
    expect(badge.textContent).not.toContain('019e937e');
  });

  it('sin folio: cae al short-id del GUID (fallback)', () => {
    const badge = renderBadge(null);

    expect(badge.textContent).toContain('RQ-019e937e');
    // El GUID completo solo vive en el tooltip (title), no en el texto.
    expect(badge.getAttribute('title')).toContain(RQ_ID);
  });
});
