import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { LiberarRevisionSheet } from './LiberarRevisionSheet';
import type { FacturaDetalle } from '../api/types';
const mutation = vi.hoisted(() => ({ mutate: vi.fn(), isPending: false }));
vi.mock('../api/useRevision', () => ({ useLiberarRevisionFactura: () => mutation }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('@/lib/api', () => ({ useFormIdempotencyKey: () => 'idempotencia-ficticia', applyServerErrors: vi.fn(), esApiError: () => false, codigoSoporte: () => undefined }));
const factura = {
  id: 'factura-ficticia', version: 4, folioProveedor: '4004', serieProveedor: 'PRB',
  revisionRepp: { pendiente: true, ultimaAutorizacionEn: null, pagos: [] },
} as unknown as FacturaDetalle;
beforeEach(() => mutation.mutate.mockReset());
it('mantiene el formulario sin liberar cuando falta el complemento', () => {
  render(<LiberarRevisionSheet open onOpenChange={vi.fn()} factura={factura} />);
  fireEvent.change(screen.getByLabelText('Acción tomada'), { target: { value: 'Aclarado con el proveedor' } });
  expect(screen.getByRole('button', { name: 'Liberar revisión' })).toBeDisabled();
  fireEvent.submit(screen.getByLabelText('Acción tomada').closest('form')!);
  expect(mutation.mutate).not.toHaveBeenCalled();
  expect(screen.getByText(/No se puede liberar mientras el complemento/)).toBeInTheDocument();
});
it('otro motivo conserva la liberación existente y su versión', async () => {
  render(<LiberarRevisionSheet open onOpenChange={vi.fn()} factura={{ ...factura, revisionRepp: null }} />);
  fireEvent.change(screen.getByLabelText('Acción tomada'), { target: { value: 'Aclarado con el proveedor' } });
  fireEvent.click(screen.getByRole('button', { name: 'Liberar revisión' }));
  await vi.waitFor(() => expect(mutation.mutate).toHaveBeenCalledWith(expect.objectContaining({ id: factura.id, versionEsperada: 4 }), expect.any(Object)));
});
