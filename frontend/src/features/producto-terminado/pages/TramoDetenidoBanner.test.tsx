import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { TramoDetenidoBanner } from './TramoDetenidoBanner';

const tramos = vi.hoisted(() => vi.fn());
vi.mock('@/features/tramos/api', () => ({ useTramos: tramos }));

beforeEach(() => vi.clearAllMocks());

describe('TramoDetenidoBanner (M-303)', () => {
  it('avisa con el motivo cuando pt.captura está detenido', () => {
    tramos.mockReturnValue({ data: [{ clave: 'pt.captura', detenido: true, motivo: 'Altas duplicadas', detenidoAt: '2026-11-11T09:00:00Z' }] });
    render(<TramoDetenidoBanner />);
    expect(screen.getByRole('note')).toHaveTextContent('La captura de producto terminado está detenida');
    expect(screen.getByRole('note')).toHaveTextContent('Altas duplicadas');
  });

  it('no muestra nada si opera, si es otro tramo o si la consulta falla', () => {
    for (const data of [[{ clave: 'pt.captura', detenido: false }], [{ clave: 'otro', detenido: true }], undefined]) {
      tramos.mockReturnValue({ data });
      const { container, unmount } = render(<TramoDetenidoBanner />);
      expect(container).toBeEmptyDOMElement();
      unmount();
    }
  });
});
