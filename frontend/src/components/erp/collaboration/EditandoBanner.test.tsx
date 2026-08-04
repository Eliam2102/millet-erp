import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { EditandoBanner } from '@/components/erp/collaboration/EditandoBanner';
import type { CollaborationPresence } from '@/components/erp/collaboration/useCollaboration';

vi.mock('@/components/erp/collaboration/useCollaboration', () => ({
  useCollaboration: vi.fn(),
}));

import { useCollaboration } from '@/components/erp/collaboration/useCollaboration';
const useCollabMock = vi.mocked(useCollaboration);

function setPresencia(p: CollaborationPresence) {
  useCollabMock.mockReturnValue(p);
}

describe('<EditandoBanner>', () => {
  it('nadie editando: render null', () => {
    setPresencia({ viendo: [{ userId: 'u-1', nombre: 'Ana' }], editando: [] });
    const { container } = render(
      <EditandoBanner entidad="requisicion" id="rq-1" />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('1 editando: mensaje en singular con nombre', () => {
    setPresencia({
      viendo: [],
      editando: [{ userId: 'u-1', nombre: 'Pedro García' }],
    });
    render(<EditandoBanner entidad="requisicion" id="rq-1" />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText('Pedro García')).toBeInTheDocument();
    expect(
      screen.getByText(/está editando esta requisición/i),
    ).toBeInTheDocument();
  });

  it('2 editando: lista con "y"', () => {
    setPresencia({
      viendo: [],
      editando: [
        { userId: 'u-1', nombre: 'Pedro' },
        { userId: 'u-2', nombre: 'Ana' },
      ],
    });
    render(<EditandoBanner entidad="requisicion" id="rq-1" />);
    expect(screen.getByText('Pedro y Ana')).toBeInTheDocument();
    expect(
      screen.getByText(/están editando esta requisición/i),
    ).toBeInTheDocument();
  });

  it('3+ editando: comma + "y" en el último', () => {
    setPresencia({
      viendo: [],
      editando: [
        { userId: 'u-1', nombre: 'Pedro' },
        { userId: 'u-2', nombre: 'Ana' },
        { userId: 'u-3', nombre: 'Carla' },
      ],
    });
    render(<EditandoBanner entidad="requisicion" id="rq-1" />);
    expect(screen.getByText('Pedro, Ana y Carla')).toBeInTheDocument();
  });
});
