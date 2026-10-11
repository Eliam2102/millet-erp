import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { toast } from 'sonner';
import { AccionesOC } from './AccionesOC';
import { ApiError } from '@/lib/api/error';
import { useAuthStore } from '@/lib/auth/auth-store';
import { createQueryWrapper } from '@/test/test-query-client';
import { EstadoOrdenCompra, type OrdenCompraDetalleResponse } from '../api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
const mocks = vi.hoisted(() => ({ ejecutar: vi.fn() }));
vi.mock('sonner', () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('../api/useEnviarAAutorizacion', () => ({ useEnviarAAutorizacion: () => ({ isPending: false, mutateAsync: mocks.ejecutar }) }));
vi.mock('../api/useAutorizarOrdenCompra', () => ({ useAutorizarOrdenCompra: () => ({ isPending: false, mutateAsync: mocks.ejecutar }) }));
beforeEach(() => { vi.clearAllMocks(); useAuthStore.setState({ permisos: [PermisosCanonicos.ComprasOrdenesCrear, PermisosCanonicos.ComprasOrdenesAutorizarNivel1] }); });
describe('Motivos de rechazo de negocio en órdenes', () => {
  it.each([
    [EstadoOrdenCompra.Borrador, 'Transmitir a autorización', 'Transmitir', 'Adjunta la cotización antes de transmitir la orden.'],
    [EstadoOrdenCompra.EnAutorizacionJefeCompras, 'Aprobar Nivel 1', 'Aprobar Nivel 1', 'Quien capturó la orden de compra no puede autorizarla.'],
  ] as const)('muestra el motivo del servidor al ejecutar %s', async (estado, boton, confirmar, motivo) => {
    mocks.ejecutar.mockRejectedValue(new ApiError({ type: 'business', title: 'Regla de negocio', status: 422, detail: motivo }, 422));
    const oc = { id: 'oc-prub', folio: 'OC-MID-1', estado, lineas: [], total: 100, adjuntos: [], autorizaciones: [] } as unknown as OrdenCompraDetalleResponse;
    render(<AccionesOC oc={oc} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: boton }));
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: confirmar }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(motivo));
  });
});
