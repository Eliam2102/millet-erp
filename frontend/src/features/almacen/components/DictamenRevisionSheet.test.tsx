import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { DictamenRevisionSheet } from '@/features/almacen/components/DictamenRevisionSheet';
import { toast } from 'sonner';
import { ApiError } from '@/lib/api';
import type { MaterialEnRevisionItem } from '@/features/almacen/api/types';

const mutate = vi.fn();
const navigate = vi.fn();

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => navigate }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock('@/features/almacen/api/useMaterialEnRevision', () => ({
  useDictaminarMaterialEnRevision: () => ({ mutate, isPending: false }),
}));
// Stub del selector de rack: expone un botón que elige un rack, sin react-query de catálogos.
vi.mock('@/components/erp/selectors/UbicacionBinSelector', () => ({
  UbicacionBinSelector: ({ onChange }: { onChange: (id: string | null) => void }) => (
    <button type="button" onClick={() => onChange('rack-1')}>
      Elegir rack
    </button>
  ),
}));

const item: MaterialEnRevisionItem = {
  lineaRevisionId: 'l-1',
  movimientoRevisionId: 'm-1',
  recepcionId: 'r-1',
  recepcionFolio: 'M-ENT2026-000001',
  fechaRecepcion: '2026-10-01',
  ordenCompraId: null,
  ordenCompraFolio: null,
  articuloId: 'a-1',
  articuloClave: 'ART-1',
  articuloDescripcion: 'Vidrio claro',
  unidadMedida: 'PZA',
  cantidadEnRevision: 2,
  cantidadDictaminada: 0,
  cantidadEnDevolucion: 0,
  cantidadPendiente: 2,
  motivoDano: 'Hojas rotas',
  ubicacionRevisionId: 'u-1',
  subAlmacenRevisionId: 's-1',
  ubicacionDisponibleSugeridaId: null,
  fechaLimite: null,
  vencida: false,
  plazoNoCalculable: false,
  dictamenes: [],
};

function abrir(onAgregarEvidencia?: () => void) {
  render(<DictamenRevisionSheet item={item} onOpenChange={() => {}} onAgregarEvidencia={onAgregarEvidencia} />, { wrapper: createQueryWrapper() });
}

function llenarMotivo() {
  fireEvent.change(screen.getByLabelText(/Motivo/), { target: { value: 'Hoja utilizable' } });
}

beforeEach(() => {
  mutate.mockReset();
  navigate.mockClear();
});

describe('<DictamenRevisionSheet>', () => {
  it('liberar sin rack muestra el error y no envía', async () => {
    abrir();
    llenarMotivo();
    fireEvent.click(screen.getByRole('button', { name: 'Liberar' }));
    expect(
      await screen.findByText('Elige la ubicación (rack) disponible a la que se libera el material.'),
    ).toBeInTheDocument();
    expect(mutate).not.toHaveBeenCalled();
  });

  it('envío correcto confirma y llama a la mutación con resultado 0 y el rack', async () => {
    abrir();
    llenarMotivo();
    fireEvent.click(screen.getByRole('button', { name: 'Elegir rack' }));
    fireEvent.click(screen.getByRole('button', { name: 'Liberar' }));

    // Irreversible: pide confirmación antes de mover inventario.
    expect(await screen.findByText(/no se puede deshacer/)).toBeInTheDocument();
    expect(mutate).not.toHaveBeenCalled();
    const botones = screen.getAllByRole('button', { name: 'Liberar' });
    fireEvent.click(botones[botones.length - 1]);

    await waitFor(() => expect(mutate).toHaveBeenCalledTimes(1));
    const arg = mutate.mock.calls[0][0];
    expect(arg.lineaRevisionId).toBe('l-1');
    expect(arg.command).toMatchObject({ resultado: 0, cantidad: 2, ubicacionDestinoId: 'rack-1', motivo: 'Hoja utilizable' });
  });

  it('no permite dictaminar más de lo pendiente', async () => {
    abrir();
    llenarMotivo();
    fireEvent.change(screen.getByLabelText(/Cantidad/), { target: { value: '3' } });
    fireEvent.click(screen.getByRole('button', { name: 'Elegir rack' }));
    fireEvent.click(screen.getByRole('button', { name: 'Liberar' }));
    expect(await screen.findByText('No puedes dictaminar más de lo pendiente (2).')).toBeInTheDocument();
    expect(mutate).not.toHaveBeenCalled();
  });

  async function rechazar() {
    abrir(() => {});
    llenarMotivo();
    fireEvent.click(screen.getByRole('radio', { name: /Rechazar al proveedor/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar al proveedor' }));
    expect(await screen.findByText(/no se puede deshacer/)).toBeInTheDocument();
    const botones = screen.getAllByRole('button', { name: 'Rechazar al proveedor' });
    fireEvent.click(botones[botones.length - 1]);
    await waitFor(() => expect(mutate).toHaveBeenCalledTimes(1));
    return mutate.mock.calls[0];
  }

  it('Rechazar oculta el rack y envía resultado 1 sin ubicación', async () => {
    const [arg] = await rechazar();
    expect(screen.queryByRole('button', { name: 'Elegir rack' })).not.toBeInTheDocument();
    expect(arg.command).toMatchObject({ resultado: 1, cantidad: 2, ubicacionDestinoId: null });
  });

  it('respuesta con devolucionAProveedorId avisa y la acción navega a la devolución', async () => {
    const [, opts] = await rechazar();
    opts.onSuccess({ dictamenId: 'd-1', resultado: 1, folio: null, devolucionAProveedorId: 'dev-9' });
    expect(toast.success).toHaveBeenCalledWith(
      'Devolución abierta para autorización de Dirección',
      expect.objectContaining({ action: expect.objectContaining({ label: 'Ver devolución' }) }),
    );
    const accion = vi.mocked(toast.success).mock.calls.at(-1)![1] as unknown as { action: { onClick: () => void } };
    accion.action.onClick();
    expect(navigate).toHaveBeenCalledWith({ to: '/almacen/devoluciones/proveedor/$id', params: { id: 'dev-9' } });
  });

  it('error DICTAMEN_RECHAZO_SIN_EVIDENCIA muestra el aviso y «Agregar evidencia»', async () => {
    const [, opts] = await rechazar();
    const detail = 'Adjunta al menos una foto o evidencia del daño antes de rechazar el material.';
    act(() => {
      opts.onError(
        new ApiError({ type: 'x', title: 'Regla de negocio', status: 422, detail, code: 'DICTAMEN_RECHAZO_SIN_EVIDENCIA' }, 422),
      );
    });
    expect(await screen.findByText(detail)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Agregar evidencia' })).toBeInTheDocument();
  });
});
