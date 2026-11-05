import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { MermasProcesoPage } from '@/features/almacen/pages/MermasProcesoPage';
import type { MermaProceso } from '@/features/almacen/api/useMermasProceso';

const hook = vi.hoisted(() => ({ useMermasProceso: vi.fn(), mutate: vi.fn() }));
const permiso = vi.hoisted(() => ({ tiene: false }));

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permiso.tiene }));
vi.mock('@/features/almacen/api/useMermasProceso', () => ({
  useMermasProceso: hook.useMermasProceso,
  useDecidirMerma: () => ({ mutate: hook.mutate, isPending: false }),
}));

const base: MermaProceso = {
  id: 'm-1',
  sucursalId: 's-1',
  tipo: 'MERMA',
  fecha: '2026-11-04',
  mesa: null,
  etapa: 'templado',
  motivo: 'Rotura en horno',
  articuloId: 'a-1',
  articuloClave: 'VCL06',
  articuloDescripcion: 'Vidrio claro 6 mm',
  kg: 12,
  m2: 0.8,
  costoUnitarioM2Mxn: 180,
  valorMxn: 144,
  estado: 'PENDIENTE',
  autorizadoPor: null,
  autorizadoAt: null,
  motivoRechazo: null,
};
const mesa: MermaProceso = {
  ...base,
  id: 'm-2',
  tipo: 'DESPERDICIO_MESA',
  mesa: 'HEGLA1',
  etapa: 'corte',
  motivo: 'Desperdicio de la mesa de corte',
  kg: null,
  m2: 3.2,
  valorMxn: 576,
  estado: 'APLICADA',
};

function datos(items: MermaProceso[]) {
  hook.useMermasProceso.mockReturnValue({ data: items, isLoading: false, isError: false, refetch: vi.fn() });
}

beforeEach(() => {
  vi.clearAllMocks();
  permiso.tiene = false;
});

describe('MermasProcesoPage', () => {
  it('lista el desperdicio por mesa y la merma con etapa, motivo y valor', () => {
    datos([base, mesa]);
    render(<MermasProcesoPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('HEGLA1')).toBeInTheDocument();
    expect(screen.getByText('Rotura en horno')).toBeInTheDocument();
    expect(screen.getByText('templado')).toBeInTheDocument();
    expect(screen.getByText('Pendiente de autorizar')).toBeInTheDocument();
  });

  it('sin facultad una merma pendiente no se puede autorizar y se explica', () => {
    datos([base]);
    render(<MermasProcesoPage />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
    expect(screen.getByText('Requiere autorización')).toBeInTheDocument();
  });

  it('con facultad Autorizar pide confirmación y manda la decisión', () => {
    permiso.tiene = true;
    datos([base]);
    render(<MermasProcesoPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar merma' }));
    expect(hook.mutate).toHaveBeenCalledWith({ id: 'm-1', accion: 'autorizar', motivo: undefined }, expect.anything());
  });

  it('lista vacía muestra el estado vacío', () => {
    datos([]);
    render(<MermasProcesoPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('Sin registros')).toBeInTheDocument();
  });
});
