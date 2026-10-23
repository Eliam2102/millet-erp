import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MotivoDialog } from './MotivoDialog';

function setup(error: string | null = null) {
  const onConfirmar = vi.fn(async () => {});
  const onCerrar = vi.fn();
  render(
    <MotivoDialog
      titulo="Rechazar viáticos"
      descripcion="Se notificará al solicitante."
      etiqueta="Motivo del rechazo"
      min={5}
      max={500}
      verbo="Rechazar viáticos"
      onConfirmar={onConfirmar}
      onCerrar={onCerrar}
      error={error}
    />,
  );
  return { onConfirmar, onCerrar };
}

describe('MotivoDialog', () => {
  it('deshabilita el verbo hasta alcanzar el mínimo y confirma con el motivo recortado', async () => {
    const { onConfirmar } = setup();
    const boton = screen.getByRole('button', { name: 'Rechazar viáticos' });
    expect(boton).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo del rechazo'), { target: { value: '  falta soporte ' } });
    expect(boton).toBeEnabled();
    fireEvent.click(boton);
    expect(onConfirmar).toHaveBeenCalledWith('falta soporte');
  });

  it('muestra el error con role=alert y cancelar llama onCerrar', async () => {
    const { onCerrar } = setup('No se pudo guardar.');
    expect(screen.getByRole('alert')).toHaveTextContent('No se pudo guardar.');
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(onCerrar).toHaveBeenCalled();
  });
});
