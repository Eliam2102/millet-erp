import { expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { NuevaCuentaSheet } from './NuevaCuentaSheet';
vi.mock('./CuentaForm', () => ({
  CuentaForm: ({
    onDirtyChange,
    onCancelar,
    onGuardada,
  }: {
    onDirtyChange: (v: boolean) => void;
    onCancelar: () => void;
    onGuardada: () => void;
  }) => (
    <>
      <input aria-label="Cuenta" onChange={() => onDirtyChange(true)} />
      <button onClick={onCancelar}>Cancelar</button>
      <button onClick={onGuardada}>Guardar</button>
    </>
  ),
}));
it('Esc sin cambios cierra directo', () => {
  const cerrar = vi.fn();
  render(<NuevaCuentaSheet open onOpenChange={cerrar} />);
  fireEvent.keyDown(document, { key: 'Escape' });
  expect(cerrar).toHaveBeenCalledWith(false);
  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
});
it('conserva captura al seguir editando; descartar cierra', () => {
  const cerrar = vi.fn();
  render(<NuevaCuentaSheet open onOpenChange={cerrar} />);
  fireEvent.change(screen.getByLabelText('Cuenta'), { target: { value: 'Cuenta nueva' } });
  fireEvent.keyDown(document, { key: 'Escape' });
  expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Seguir editando' }));
  expect(cerrar).not.toHaveBeenCalled();
  expect(screen.getByLabelText('Cuenta')).toHaveValue('Cuenta nueva');
  fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
  fireEvent.click(screen.getByRole('button', { name: 'Descartar' }));
  expect(cerrar).toHaveBeenCalledWith(false);
});
it('guardar cierra sin preguntar', () => {
  const cerrar = vi.fn();
  render(<NuevaCuentaSheet open onOpenChange={cerrar} />);
  fireEvent.change(screen.getByLabelText('Cuenta'), { target: { value: 'Cuenta nueva' } });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
  expect(cerrar).toHaveBeenCalledWith(false);
  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
});
