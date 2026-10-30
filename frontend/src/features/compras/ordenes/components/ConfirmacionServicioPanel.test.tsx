import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createQueryWrapper } from '@/test/test-query-client';
import { EstadoOrdenCompra, type OrdenCompraDetalleResponse } from '../api/types';
import { ConfirmacionServicioPanel } from './ConfirmacionServicioPanel';

const permiso = vi.hoisted(() => ({ value: true }));
const apiRequest = vi.hoisted(() => vi.fn());
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permiso.value }));
vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest,
}));

function oc(extra: Partial<OrdenCompraDetalleResponse> = {}, esServicio = true): OrdenCompraDetalleResponse {
  return {
    id: 'oc-1',
    estado: EstadoOrdenCompra.Autorizada,
    lineas: [{ id: 'l-1', posicion: 1, esServicio, cantidad: 1, cantidadRecibida: 0 }],
    ...extra,
  } as unknown as OrdenCompraDetalleResponse;
}

function pintar(o: OrdenCompraDetalleResponse) {
  return render(<ConfirmacionServicioPanel oc={o} />, { wrapper: createQueryWrapper() });
}

describe('Conformidad del servicio por OC (86e3tacbn)', () => {
  beforeEach(() => {
    permiso.value = true;
    apiRequest.mockReset().mockResolvedValue({ data: undefined });
  });

  it('no aparece en una OC sin líneas de servicio', () => {
    pintar(oc({}, false));
    expect(screen.queryByLabelText('Conformidad del servicio')).toBeNull();
  });

  it('marcar la casilla pide comentario opcional y confirma con un solo POST por OC', async () => {
    pintar(oc());
    fireEvent.click(screen.getByRole('checkbox', { name: /servicio realizado y recibido a satisfacción/i }));
    fireEvent.change(screen.getByLabelText(/Comentario/), { target: { value: 'Todo en orden' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar servicio' }));
    await waitFor(() => expect(apiRequest).toHaveBeenCalledTimes(1));
    expect(apiRequest.mock.calls[0][0]).toBe('/api/v1/compras/ordenes/oc-1/confirmar-servicio');
    expect(apiRequest.mock.calls[0][1].body).toEqual({ comentario: 'Todo en orden' });
  });

  it('deshacer exige motivo y no llama al backend sin él', async () => {
    pintar(oc({ servicioConfirmadoEn: '2026-10-30T12:00:00Z', servicioConfirmadoPorNombre: 'Ana' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /servicio realizado y recibido a satisfacción/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Deshacer confirmación' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Indica el motivo');
    expect(apiRequest).not.toHaveBeenCalled();
  });

  it('con una factura pagada la confirmación no se puede deshacer', () => {
    pintar(oc({ servicioConfirmadoEn: '2026-10-30T12:00:00Z', montoPagado: 100 }));
    expect(screen.getByRole('checkbox', { name: /servicio realizado/i })).toBeDisabled();
    expect(screen.getByText(/ya tiene una factura pagada/)).toBeInTheDocument();
  });

  it('«No quedé conforme» envía el motivo a Compras', async () => {
    pintar(oc());
    fireEvent.click(screen.getByRole('button', { name: 'No quedé conforme' }));
    fireEvent.change(screen.getByLabelText(/Motivo/), { target: { value: 'Sin informe final' } });
    fireEvent.click(screen.getByRole('button', { name: 'No quedé conforme' }));
    await waitFor(() => expect(apiRequest).toHaveBeenCalledTimes(1));
    expect(apiRequest.mock.calls[0][0]).toBe('/api/v1/compras/ordenes/oc-1/servicio-no-conforme');
  });

  it('sin el permiso la casilla queda deshabilitada y explica quién puede confirmar', () => {
    permiso.value = false;
    pintar(oc());
    expect(screen.getByRole('checkbox', { name: /servicio realizado/i })).toBeDisabled();
    expect(screen.getByText(/Solo el área que pidió el servicio/)).toBeInTheDocument();
  });
});
