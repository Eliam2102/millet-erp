import { expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ExpedienteForm } from './ExpedienteForm';
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('../api/hooks', () => ({
  useCrearExpediente: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useOrdenesImportacion: () => ({ data: [] }),
}));
it('sin cambios Esc cierra directamente', () => {
  const cerrar = vi.fn();
  render(<ExpedienteForm onClose={cerrar} />, { wrapper: createQueryWrapper() });
  fireEvent.keyDown(document, { key: 'Escape' });
  expect(cerrar).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
});
it('mantiene la referencia al seguir editando y descarta solo con aceptación', () => {
  const cerrar = vi.fn();
  render(<ExpedienteForm onClose={cerrar} />, { wrapper: createQueryWrapper() });
  fireEvent.change(screen.getByLabelText(/Referencia/), {
    target: { value: 'Referencia capturada' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
  expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Seguir editando' }));
  expect(cerrar).not.toHaveBeenCalled();
  expect(screen.getByLabelText(/Referencia/)).toHaveValue('Referencia capturada');
  fireEvent.keyDown(document, { key: 'Escape' });
  fireEvent.click(screen.getByRole('button', { name: 'Descartar' }));
  expect(cerrar).toHaveBeenCalledTimes(1);
});
