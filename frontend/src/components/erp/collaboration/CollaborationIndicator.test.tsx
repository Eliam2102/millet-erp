import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CollaborationIndicator } from '@/components/erp/collaboration/CollaborationIndicator';
import type { CollaborationPresence } from '@/components/erp/collaboration/useCollaboration';

// Mock del hook — los tests del hook real cubren la conexión SignalR;
// aquí solo validamos el render según presencia provista.
vi.mock('@/components/erp/collaboration/useCollaboration', () => ({
  useCollaboration: vi.fn(),
}));

import { useCollaboration } from '@/components/erp/collaboration/useCollaboration';
const useCollabMock = vi.mocked(useCollaboration);

function setPresencia(p: CollaborationPresence) {
  useCollabMock.mockReturnValue(p);
}

describe('<CollaborationIndicator>', () => {
  it('presencia vacía: renderiza null (sin ruido visual)', () => {
    setPresencia({ viendo: [], editando: [] });
    const { container } = render(
      <CollaborationIndicator entidad="requisicion" id="rq-1" />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('1 viendo: muestra avatar con iniciales', () => {
    setPresencia({
      viendo: [{ userId: 'u-1', nombre: 'Pedro García' }],
      editando: [],
    });
    render(<CollaborationIndicator entidad="requisicion" id="rq-1" />);
    // Iniciales "PG"
    expect(screen.getByText('PG')).toBeInTheDocument();
  });

  it('1 editando: avatar tiene icono pencil + ring ámbar', () => {
    setPresencia({
      viendo: [],
      editando: [{ userId: 'u-1', nombre: 'Pedro García' }],
    });
    const { container } = render(
      <CollaborationIndicator entidad="requisicion" id="rq-1" />,
    );
    // El badge animado de "editando" tiene clase amber.
    expect(container.querySelector('[class*="amber"]')).not.toBeNull();
  });

  it('4 usuarios: muestra solo 3 avatares + badge "+1"', () => {
    setPresencia({
      viendo: [
        { userId: 'u-1', nombre: 'Ana López' },
        { userId: 'u-2', nombre: 'Beto Ríos' },
      ],
      editando: [
        { userId: 'u-3', nombre: 'Carla Mez' },
        { userId: 'u-4', nombre: 'Diego Sol' },
      ],
    });
    render(<CollaborationIndicator entidad="requisicion" id="rq-1" />);
    expect(screen.getByText('+1')).toBeInTheDocument();
  });

  it('aria-label resume editando + viendo', () => {
    setPresencia({
      viendo: [
        { userId: 'u-1', nombre: 'Ana' },
        { userId: 'u-2', nombre: 'Beto' },
      ],
      editando: [{ userId: 'u-3', nombre: 'Carla' }],
    });
    render(<CollaborationIndicator entidad="requisicion" id="rq-1" />);
    const status = screen.getByRole('status');
    expect(status).toHaveAttribute(
      'aria-label',
      expect.stringMatching(/1 persona editando.*2 personas mirando/i),
    );
  });
});
