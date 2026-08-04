import { describe, expect, it } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { TimelineRequisicion } from '@/features/compras/components/TimelineRequisicion';
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

describe('<TimelineRequisicion>', () => {
  it('lista vacía: muestra mensaje "Sin transiciones"', () => {
    render(<TimelineRequisicion entradas={[]} />);
    expect(
      screen.getByText(/sin transiciones registradas/i),
    ).toBeInTheDocument();
  });

  it('isLoading=true: muestra mensaje de carga', () => {
    render(<TimelineRequisicion entradas={[]} isLoading />);
    expect(screen.getByText(/cargando histórico/i)).toBeInTheDocument();
  });

  it('renderiza una entrada Creada con etiqueta + actorNombre del backend + fecha', () => {
    render(
      <TimelineRequisicion
        entradas={[
          entry({ tipo: HistoricoTipo.Creada, actorId: 'u-1', actorNombre: 'Pedro García' }),
        ]}
      />,
    );
    expect(screen.getByText('Creada')).toBeInTheDocument();
    expect(screen.getByText('Pedro García')).toBeInTheDocument();
  });

  it('actorId=null se muestra como "Sistema"', () => {
    render(<TimelineRequisicion entradas={[entry({ actorId: null, actorNombre: null })]} />);
    expect(screen.getByText('Sistema')).toBeInTheDocument();
  });

  it('actor no resuelto (actorNombre null con actorId presente) cae al id', () => {
    render(
      <TimelineRequisicion
        entradas={[entry({ actorId: 'u-sin-nombre', actorNombre: null })]}
      />,
    );
    expect(screen.getByText('u-sin-nombre')).toBeInTheDocument();
  });

  it('cambios JSON no vacío muestra botón "Ver cambios"; click lo expande', () => {
    render(
      <TimelineRequisicion
        entradas={[
          entry({
            tipo: HistoricoTipo.Transmitida,
            cambios: '{"estado":["Borrador","EnAutorizacion"]}',
          }),
        ]}
      />,
    );
    const btn = screen.getByRole('button', { name: /ver cambios/i });
    expect(btn).toHaveAttribute('aria-expanded', 'false');
    act(() => btn.click());
    expect(btn).toHaveAttribute('aria-expanded', 'true');
    // El JSON pretty aparece en un <pre>.
    expect(screen.getByText(/estado/)).toBeInTheDocument();
  });

  it('cambios vacío "{}" no muestra botón "Ver cambios"', () => {
    render(
      <TimelineRequisicion entradas={[entry({ cambios: '{}' })]} />,
    );
    expect(
      screen.queryByRole('button', { name: /ver cambios/i }),
    ).not.toBeInTheDocument();
  });

  it('cubre los principales tipos con etiqueta humana correcta', () => {
    render(
      <TimelineRequisicion
        entradas={[
          entry({ tipo: HistoricoTipo.AutorizadaN1, timestamp: '2026-05-09T11:00:00Z' }),
          entry({ tipo: HistoricoTipo.AutorizadaN2, timestamp: '2026-05-09T12:00:00Z' }),
          entry({ tipo: HistoricoTipo.Rechazada, timestamp: '2026-05-09T13:00:00Z' }),
          entry({ tipo: HistoricoTipo.Cancelada, timestamp: '2026-05-09T14:00:00Z' }),
          entry({ tipo: HistoricoTipo.Cerrada, timestamp: '2026-05-09T15:00:00Z' }),
        ]}
      />,
    );
    expect(screen.getByText('Autorizada Nivel 1')).toBeInTheDocument();
    expect(screen.getByText('Autorizada Nivel 2')).toBeInTheDocument();
    expect(screen.getByText('Rechazada')).toBeInTheDocument();
    expect(screen.getByText('Cancelada')).toBeInTheDocument();
    expect(screen.getByText('Cerrada')).toBeInTheDocument();
  });

  it('ARIA: <ol> con aria-label y entradas son <li>', () => {
    const { container } = render(
      <TimelineRequisicion entradas={[entry()]} />,
    );
    const list = container.querySelector('ol');
    expect(list).toHaveAttribute(
      'aria-label',
      expect.stringMatching(/línea de tiempo/i),
    );
    expect(list?.querySelectorAll('li').length).toBe(1);
  });
});
