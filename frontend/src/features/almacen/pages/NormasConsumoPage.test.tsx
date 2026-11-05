import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { NormasConsumoPage } from '@/features/almacen/pages/NormasConsumoPage';
import type { NormaConsumo } from '@/features/almacen/api/useConsumoComponentes';

const hook = vi.hoisted(() => ({ useNormasConsumo: vi.fn(), mutate: vi.fn() }));
const permiso = vi.hoisted(() => ({ tiene: false }));

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permiso.tiene }));
vi.mock('@/features/almacen/api/useConsumoComponentes', () => ({
  useNormasConsumo: hook.useNormasConsumo,
  useGuardarNormaConsumo: () => ({ mutate: hook.mutate, isPending: false }),
}));
vi.mock('@/components/erp', async (orig) => ({ ...(await orig<object>()), ArticuloSelector: () => <div /> }));

const argon: NormaConsumo = {
  articuloId: 'a-1',
  articuloClave: 'ARGON',
  articuloDescripcion: 'Gas argón',
  unidadMedida: 'M3',
  consumoPorUnidad: 1,
  porcentajeMerma: 10,
  factorM3PorM2: 0.012,
  updatedAt: '2026-11-05T15:00:00Z',
};

function datos(items: NormaConsumo[]) {
  hook.useNormasConsumo.mockReturnValue({ data: items, isLoading: false, isError: false, refetch: vi.fn() });
}

beforeEach(() => {
  vi.clearAllMocks();
  permiso.tiene = false;
});

describe('NormasConsumoPage', () => {
  it('lista la norma con consumo, merma y factor; sin facultad no se puede editar', () => {
    datos([argon]);
    render(<NormasConsumoPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('ARGON')).toBeInTheDocument();
    expect(screen.getByText('0.012')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Editar norma/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Nueva norma/ })).not.toBeInTheDocument();
  });

  it('catálogo vacío lo dice', () => {
    datos([]);
    render(<NormasConsumoPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('Aún no hay normas')).toBeInTheDocument();
  });

  it('con facultad edita y manda el PUT con el factor del argón', () => {
    permiso.tiene = true;
    datos([argon]);
    render(<NormasConsumoPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /Editar norma de ARGON/ }));
    fireEvent.change(screen.getByLabelText(/Merma %/), { target: { value: '12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar norma' }));
    expect(hook.mutate).toHaveBeenCalledWith(
      { articuloId: 'a-1', captura: { consumoPorUnidad: 1, porcentajeMerma: 12, factorM3PorM2: 0.012 } },
      expect.anything(),
    );
  });

  it('un artículo en m³ sin factor no se guarda', () => {
    permiso.tiene = true;
    datos([{ ...argon, factorM3PorM2: null }]);
    render(<NormasConsumoPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('Falta')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Editar norma de ARGON/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Guardar norma' }));
    expect(hook.mutate).not.toHaveBeenCalled();
    expect(screen.getByText(/captura el factor/)).toBeInTheDocument();
  });
});
