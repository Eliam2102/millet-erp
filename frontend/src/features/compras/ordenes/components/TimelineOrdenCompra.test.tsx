import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TimelineOrdenCompra } from './TimelineOrdenCompra';

const { historicoMock } = vi.hoisted(() => ({ historicoMock: vi.fn() }));

vi.mock('@/features/compras/ordenes/api/useHistoricoOrdenCompra', () => ({
  useHistoricoOrdenCompra: historicoMock,
}));

vi.mock('@/features/catalogos/api', () => ({
  useUsuarios: () => ({ data: undefined }),
  mapById: () => new Map(),
}));

const eventos = [
  {
    id: 'e1',
    timestamp: '2026-10-09T12:00:00Z',
    operacion: 'crear',
    entidad: 'AutorizacionOC',
    usuarioId: null,
    resumen: 'Creó AutorizacionOC AutorizacionOC 01a12f96',
  },
  {
    id: 'e2',
    timestamp: '2026-10-09T12:05:00Z',
    operacion: 'crear',
    entidad: 'LineaOrdenCompra',
    usuarioId: null,
    resumen: 'Creó LineaOrdenCompra LineaOrdenCompra 01a12ebb',
  },
];

describe('TimelineOrdenCompra', () => {
  beforeEach(() => {
    historicoMock.mockReturnValue({
      isLoading: false,
      isError: false,
      data: { eventos },
      refetch: vi.fn(),
    });
  });

  it('muestra encabezado y resumen en español sin texto técnico', () => {
    render(<TimelineOrdenCompra ocId="oc-1" />);
    expect(screen.getByText('Se creó la firma de autorización')).toBeInTheDocument();
    expect(screen.getByText('Creó la firma de autorización')).toBeInTheDocument();
    expect(screen.getByText('Creó una línea de la orden')).toBeInTheDocument();
    expect(screen.queryByText(/AutorizacionOC AutorizacionOC/)).not.toBeInTheDocument();
    expect(screen.queryByText(/01a12f96/)).not.toBeInTheDocument();
  });

  it('conserva el resumen original en title y el filtro por entidad', () => {
    render(<TimelineOrdenCompra ocId="oc-1" filtroEntidad="AutorizacionOC" />);
    expect(screen.queryByText('Se creó una línea de la orden')).not.toBeInTheDocument();
    const resumen = screen.getByText('Creó la firma de autorización');
    expect(resumen).toHaveAttribute('title', 'Creó AutorizacionOC AutorizacionOC 01a12f96');
  });
});
