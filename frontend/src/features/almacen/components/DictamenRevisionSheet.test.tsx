import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { DictamenRevisionSheet } from '@/features/almacen/components/DictamenRevisionSheet';
import type { MaterialEnRevisionItem } from '@/features/almacen/api/types';

const mutate = vi.fn();

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
  dictamenes: [],
};

function abrir() {
  render(<DictamenRevisionSheet item={item} onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
}

function llenarMotivo() {
  fireEvent.change(screen.getByLabelText(/Motivo/), { target: { value: 'Hoja utilizable' } });
}

beforeEach(() => mutate.mockClear());

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
});
