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
  useAdoptarCfdi: () => ({ isPending: false, mutate: vi.fn(), reset: vi.fn() }),
}));

it('muestra el estado recibido con su texto de usuario al reintentar', () => {
  render(<TimbradoFallidoBanner comprobanteId="prueba" estado="TimbradoFallido" errorCodigo={null} errorMensaje={null} />);
  fireEvent.click(screen.getByRole('button', { name: 'Reintentar timbrado' }));
  expect(toast.info).toHaveBeenCalledWith(
    'Estado del comprobante: Timbrado en proceso. Consulta el comprobante antes de reintentar.',
  );
});

it('M19: sin saldo del proveedor de timbrado no pide revisar los datos fiscales', () => {
  render(<TimbradoFallidoBanner comprobanteId="prueba" estado="TimbradoFallido" errorCodigo="PAC_SIN_SALDO" errorMensaje={null} />);
  expect(screen.getByText(/El proveedor de timbrado no tiene saldo para timbrar/)).toBeInTheDocument();
  expect(screen.queryByText(/Revisa sus datos fiscales/)).not.toBeInTheDocument();
});

it('M-284: ofrece adoptar el CFDI solo cuando el PAC no confirmó el timbrado', () => {
  const { rerender } = render(
    <TimbradoFallidoBanner comprobanteId="prueba" estado="TimbradoFallido" errorCodigo="PAC_TIMEOUT" errorMensaje={null} />,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Adoptar CFDI timbrado' }));
  expect(screen.getByRole('dialog', { name: /Adoptar CFDI timbrado/ })).toBeTruthy();

  rerender(
    <TimbradoFallidoBanner comprobanteId="otro" estado="TimbradoFallido" errorCodigo="CFDI40102" errorMensaje={null} />,
  );
  expect(screen.queryByRole('button', { name: 'Adoptar CFDI timbrado' })).toBeNull();
});

it('H-U34: en Carta Porte muestra el código y el mensaje con que el PAC rechazó el timbrado', () => {
  render(
    <TimbradoFallidoBanner
      comprobanteId="prueba"
      estado="TimbradoFallido"
      errorCodigo="CP103"
      errorMensaje="El valor del campo Moneda debe ser XXX para el tipo de comprobante T."
      mostrarMotivoPac
    />,
  );
  expect(screen.getByText('Motivo del PAC:')).toBeInTheDocument();
  expect(screen.getByText('CP103')).toBeInTheDocument();
  expect(screen.getByText(/El valor del campo Moneda debe ser XXX/)).toBeInTheDocument();
});

it('H-U34: sin mensaje del PAC no muestra el renglón de motivo', () => {
  render(
    <TimbradoFallidoBanner comprobanteId="prueba" estado="TimbradoFallido" errorCodigo="PAC_SIN_SALDO" errorMensaje={null} mostrarMotivoPac />,
  );
  expect(screen.queryByText('Motivo del PAC:')).toBeNull();
});

it('fuera de Carta Porte (facturas) el texto técnico del PAC sigue oculto', () => {
  render(<TimbradoFallidoBanner comprobanteId="prueba" estado="TimbradoFallido" errorCodigo="CFDI40100" errorMensaje="Rechazo del PAC" />);
  expect(screen.queryByText(/Motivo del PAC|CFDI40100|Rechazo del PAC/)).toBeNull();
});
