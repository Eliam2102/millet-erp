import type { ComponentProps } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ConfirmarAccionDialog } from './ConfirmarAccionDialog';

function setup(over: Partial<ComponentProps<typeof ConfirmarAccionDialog>> = {}) {
  const onConfirmar = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <ConfirmarAccionDialog
      open
      onOpenChange={onOpenChange}
      titulo="Eliminar adjunto"
      resumen={<span>factura.pdf</span>}
      verbo="Eliminar adjunto"
      tono="peligro"
      onConfirmar={onConfirmar}
      {...over}
    />,
  );
  return { onConfirmar, onOpenChange };
}

describe('ConfirmarAccionDialog', () => {
  it('muestra resumen y confirma con el verbo', async () => {
    const { onConfirmar } = setup();
    expect(screen.getByText('factura.pdf')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Eliminar adjunto' }));
    expect(onConfirmar).toHaveBeenCalledOnce();
  });

  it('cancelar cierra sin confirmar', async () => {
    const { onConfirmar, onOpenChange } = setup();
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onConfirmar).not.toHaveBeenCalled();
  });

  it('pendiente deshabilita y muestra «…»', () => {
    setup({ pendiente: true });
    expect(screen.getByRole('button', { name: '…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
  });
});
