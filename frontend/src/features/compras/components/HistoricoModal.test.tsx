import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { HistoricoModal } from '@/features/compras/components/HistoricoModal';
import {
  HistoricoTipo,
  type HistoricoEntryResponse,
} from '@/features/compras/api/types';

function entry(
  overrides: Partial<HistoricoEntryResponse> = {},
): HistoricoEntryResponse {
  return {
    tipo: HistoricoTipo.Creada,
    operacion: 'crear',
    entidad: 'Requisicion',
    entidadId: 'rq-1',
    actorId: 'u-1',
    actorNombre: null,
    timestamp: '2026-05-09T10:00:00Z',
    cambios: '{}',
    correlationId: 'corr-1',
    ...overrides,
  };
}

describe('<HistoricoModal>', () => {
  it('open=false: no renderiza el dialog', () => {
    render(
      <HistoricoModal
        open={false}
        onOpenChange={() => {}}
        folio="MID2026-000042"
        entradas={[]}
      />,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('open=true: muestra título con folio y descripción', () => {
    render(
      <HistoricoModal
        open={true}
        onOpenChange={() => {}}
        folio="MID2026-000042"
        entradas={[]}
      />,
    );
    expect(
      screen.getByRole('heading', { name: /histórico/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('MID2026-000042')).toBeInTheDocument();
    expect(
      screen.getByText(/transiciones del agregado/i),
    ).toBeInTheDocument();
  });

  it('lista vacía: muestra "Sin transiciones registradas"', () => {
    render(
      <HistoricoModal
        open={true}
        onOpenChange={() => {}}
        folio="MID2026-000042"
        entradas={[]}
      />,
    );
    expect(
      screen.getByText(/sin transiciones registradas/i),
    ).toBeInTheDocument();
  });

  it('isLoading: muestra mensaje de carga', () => {
    render(
      <HistoricoModal
        open={true}
        onOpenChange={() => {}}
        folio="MID2026-000042"
        entradas={[]}
        isLoading
      />,
    );
    expect(screen.getByText(/cargando histórico/i)).toBeInTheDocument();
  });

  it('renderiza N entradas con etiquetas humanas', () => {
    render(
      <HistoricoModal
        open={true}
        onOpenChange={() => {}}
        folio="MID2026-000042"
        entradas={[
          entry({ tipo: HistoricoTipo.Creada, timestamp: '2026-05-09T10:00:00Z', actorNombre: 'Pedro García' }),
          entry({ tipo: HistoricoTipo.Transmitida, timestamp: '2026-05-09T11:00:00Z', actorNombre: 'Pedro García' }),
          entry({ tipo: HistoricoTipo.AutorizadaN1, timestamp: '2026-05-09T12:00:00Z', actorNombre: 'Pedro García' }),
        ]}
      />,
    );
    expect(screen.getByText('Creada')).toBeInTheDocument();
    expect(screen.getByText('Transmitida a autorización')).toBeInTheDocument();
    expect(screen.getByText('Autorizada Nivel 1')).toBeInTheDocument();
    // Actor resuelto en backend (actorNombre) en al menos una entrada.
    expect(screen.getAllByText('Pedro García').length).toBeGreaterThan(0);
  });

  it('onOpenChange se invoca al cerrar (ESC o click fuera)', () => {
    const onOpenChange = vi.fn();
    render(
      <HistoricoModal
        open={true}
        onOpenChange={onOpenChange}
        folio="MID2026-000042"
        entradas={[]}
      />,
    );
    // Radix Dialog expone botón "Close" con sr-only label.
    const closeBtn = screen.getByRole('button', { name: /close/i });
    closeBtn.click();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
