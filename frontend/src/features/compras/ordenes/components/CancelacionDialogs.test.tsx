import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DobleFirmaDialog } from './DobleFirmaDialog';
import { ResolverCancelacionDialog } from './ResolverCancelacionDialog';

vi.mock('@/features/compras/components/MotivoRechazoSelector', () => ({
  MotivoRechazoAplicaA: { Cancelacion: 4 },
  MotivoRechazoSelector: ({ onChange }: { onChange: (id: string) => void }) =>
    <button type="button" onClick={() => onChange('00000000-0000-0000-0000-000000000001')}>Elegir motivo</button>,
}));

describe('Diálogos de cancelación P2', () => {
  it('solicita una firma con motivo, sin casillas de representación', async () => {
    const submit = vi.fn().mockResolvedValue(undefined);
    render(<DobleFirmaDialog open onOpenChange={vi.fn()} ocFolio="OC-P2" onSubmit={submit} />);
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.getByText(/Dirección deberá confirmar/)).toBeInTheDocument();
    fireEvent.click(screen.getByText('Elegir motivo'));
    fireEvent.change(screen.getByLabelText('Detalle del motivo'), { target: { value: 'Cancelar el faltante' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar cancelación' }));
    await waitFor(() => expect(submit).toHaveBeenCalledWith({ motivoCancelacionId: '00000000-0000-0000-0000-000000000001', motivoCancelacionTexto: 'Cancelar el faltante' }));
  });
  it.each([true, false])('envía la decisión de Dirección y su motivo: %s', async (confirmar) => {
    const submit = vi.fn().mockResolvedValue(undefined);
    render(<ResolverCancelacionDialog confirmar={confirmar} onClose={vi.fn()} onSubmit={submit} isPending={false} />);
    fireEvent.change(screen.getByLabelText('Motivo de la decisión'), { target: { value: 'Decisión documentada' } });
    fireEvent.click(screen.getByRole('button', { name: confirmar ? 'Confirmar cancelación' : 'Rechazar cancelación' }));
    await waitFor(() => expect(submit).toHaveBeenCalledWith({ confirmar, motivo: 'Decisión documentada' }));
  });

  it('no registra una solicitud con motivo en blanco', () => {
    const submit = vi.fn();
    render(<DobleFirmaDialog open onOpenChange={vi.fn()} ocFolio="OC-P2" onSubmit={submit} />);
    fireEvent.click(screen.getByText('Elegir motivo'));
    fireEvent.change(screen.getByLabelText('Detalle del motivo'), { target: { value: '   ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar cancelación' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Escribe el motivo de la solicitud.');
    expect(submit).not.toHaveBeenCalled();
  });

  it.each([true, false])('exige motivo para la decisión: %s', (confirmar) => {
    const submit = vi.fn();
    render(<ResolverCancelacionDialog confirmar={confirmar} onClose={vi.fn()} onSubmit={submit} isPending={false} />);
    fireEvent.change(screen.getByLabelText('Motivo de la decisión'), { target: { value: '   ' } });
    fireEvent.click(screen.getByRole('button', { name: confirmar ? 'Confirmar cancelación' : 'Rechazar cancelación' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Escribe el motivo de tu decisión.');
    expect(submit).not.toHaveBeenCalled();
  });

  it('impide repetir o abandonar una firma mientras se guarda', () => {
    render(<ResolverCancelacionDialog confirmar onClose={vi.fn()} onSubmit={vi.fn()} isPending />);
    expect(screen.getByRole('button', { name: 'Confirmar cancelación' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Volver' })).toBeDisabled();
  });
});
