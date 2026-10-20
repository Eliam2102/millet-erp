import { beforeEach, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { RfcReceptorList } from './RfcReceptorList';
const mocks = vi.hoisted(() => ({ eliminar: vi.fn(), pending: false }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/lib/api', () => ({ useFormIdempotencyKey: () => 'test-key' }));
vi.mock('./SubirFielDialog', () => ({ SubirFielDialog: () => null }));
vi.mock('@/features/integraciones-fiscal/api/useIntegracionesFiscal', () => ({
  useRfcsReceptores: () => ({
    data: [
      {
        id: 'rfc-1',
        empresaId: 'empresa-1',
        rfc: 'AAA010101AAA',
        tieneFiel: false,
        descargaHabilitada: true,
        refreshHabilitada: false,
      },
    ],
  }),
  useEliminarRfcReceptor: () => ({ mutate: mocks.eliminar, isPending: mocks.pending }),
  useActualizarRfcReceptor: () => ({ mutate: vi.fn() }),
  useAgregarRfcReceptor: () => ({ mutate: vi.fn() }),
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.pending = false;
});
it('abre la confirmación, cancelar conserva el RFC y confirmar elimina solo al aceptar', () => {
  const view = render(<RfcReceptorList empresaId="empresa-1" />);
  fireEvent.click(screen.getByRole('button', { name: 'Eliminar RFC AAA010101AAA' }));
  expect(screen.getByRole('alertdialog')).toHaveTextContent('AAA010101AAA');
  expect(mocks.eliminar).not.toHaveBeenCalled();
  fireEvent.click(
    within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancelar' }),
  );
  expect(mocks.eliminar).not.toHaveBeenCalled();
  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Eliminar RFC AAA010101AAA' }));
  fireEvent.click(
    within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Eliminar RFC' }),
  );
  expect(mocks.eliminar).toHaveBeenCalledWith(
    { id: 'rfc-1', empresaId: 'empresa-1', idempotencyKey: 'test-key' },
    expect.any(Object),
  );
  expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  mocks.pending = true;
  view.rerender(<RfcReceptorList empresaId="empresa-1" />);
  expect(
    within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancelar' }),
  ).toBeDisabled();
  expect(within(screen.getByRole('alertdialog')).getByRole('button', { name: '…' })).toBeDisabled();
  act(() => mocks.eliminar.mock.calls[0][1].onSuccess());
  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
});

it('si la eliminación falla mantiene abierto el diálogo para reintentar', () => {
  render(<RfcReceptorList empresaId="empresa-1" />);
  fireEvent.click(screen.getByRole('button', { name: 'Eliminar RFC AAA010101AAA' }));
  fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Eliminar RFC' }));
  act(() => mocks.eliminar.mock.calls[0][1].onError());
  expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  expect(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Eliminar RFC' })).toBeEnabled();
});
