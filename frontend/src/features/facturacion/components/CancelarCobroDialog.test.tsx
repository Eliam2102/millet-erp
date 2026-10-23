import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CancelarCobroDialog } from './CancelarCobroDialog';

describe('CancelarCobroDialog', () => {
  it('pide un motivo de al menos 5 caracteres antes de cancelar el cobro', async () => {
    const onConfirmar = vi.fn().mockResolvedValue(undefined);
    render(
      <CancelarCobroDialog
        folio="FIX12-UZ-F4"
        total="$232.00"
        onConfirmar={onConfirmar}
        onCerrar={vi.fn()}
        error={null}
      />,
    );

    expect(screen.getByRole('dialog', { name: 'Cancelar el cobro' })).toBeInTheDocument();
    expect(screen.getByText(/FIX12-UZ-F4 · \$232\.00/)).toBeInTheDocument();
    const confirmar = screen.getByRole('button', { name: 'Cancelar cobro' });
    expect(confirmar).toBeDisabled();

    const motivo = screen.getByLabelText(/Motivo de la cancelación/);
    fireEvent.change(motivo, { target: { value: 'Err' } });
    expect(confirmar).toBeDisabled();

    fireEvent.change(motivo, { target: { value: '  Error de captura  ' } });
    expect(confirmar).toBeEnabled();
    fireEvent.click(confirmar);

    await waitFor(() => expect(onConfirmar).toHaveBeenCalledWith('Error de captura'));
  });

  it('muestra el error del backend y no se cierra', () => {
    render(
      <CancelarCobroDialog
        folio="FIX12-UZ-F4"
        total="$232.00"
        onConfirmar={vi.fn()}
        onCerrar={vi.fn()}
        error="Solo un cobro Registrado puede cancelarse."
      />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Solo un cobro Registrado puede cancelarse.',
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});
