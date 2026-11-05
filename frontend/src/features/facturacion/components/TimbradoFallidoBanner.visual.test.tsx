import { fireEvent, render, screen } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { TimbradoFallidoBanner } from './TimbradoFallidoBanner';

vi.mock('sonner', () => ({ toast: { info: vi.fn(), success: vi.fn(), error: vi.fn() } }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/features/facturacion/api/useFacturas', () => ({
  useReintentarTimbrado: () => ({
    isPending: false,
    error: null,
    mutate: (_args: unknown, opciones: { onSuccess: (r: { estado: string }) => void }) =>
      opciones.onSuccess({ estado: 'TimbradoEnProceso' }),
  }),
  useDescartarComprobante: () => ({ isPending: false, mutate: vi.fn() }),
}));

it('muestra el estado recibido con su texto de usuario al reintentar', () => {
  render(<TimbradoFallidoBanner comprobanteId="prueba" estado="TimbradoFallido" errorCodigo={null} errorMensaje={null} />);
  fireEvent.click(screen.getByRole('button', { name: 'Reintentar timbrado' }));
  expect(toast.info).toHaveBeenCalledWith(
    'Estado del comprobante: Timbrado en proceso. Consulta el comprobante antes de reintentar.',
  );
});
