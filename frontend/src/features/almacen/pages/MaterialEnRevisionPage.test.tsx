import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { MaterialEnRevisionPage } from '@/features/almacen/pages/MaterialEnRevisionPage';
import type { MaterialEnRevisionItem } from '@/features/almacen/api/types';

const hook = vi.hoisted(() => ({ useMaterialEnRevision: vi.fn() }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...rest }: { children: React.ReactNode } & Record<string, unknown>) => (
    <a href="#" aria-label={rest['aria-label'] as string}>
      {children}
    </a>
  ),
}));
vi.mock('@/features/almacen/api/useMaterialEnRevision', () => hook);
vi.mock('@/components/erp/adjuntos/DocumentoAdjuntosSection', () => ({
  DocumentoAdjuntosSection: () => <div>Adjuntos</div>,
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
  fechaLimite: '2026-10-08T00:00:00Z',
  vencida: true,
};

function datos(items: MaterialEnRevisionItem[]) {
  hook.useMaterialEnRevision.mockReturnValue({
    data: items,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  });
}

beforeEach(() => vi.clearAllMocks());

describe('MaterialEnRevisionPage', () => {
  it('muestra el folio y «Vencido» de una línea fuera de plazo', () => {
    datos([item]);
    render(<MaterialEnRevisionPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('M-ENT2026-000001')).toBeInTheDocument();
    expect(screen.getByText('Vencido')).toBeInTheDocument();
    expect(screen.getByText('Hojas rotas')).toBeInTheDocument();
  });

  it('«Todos» consulta con soloPendientes false', () => {
    datos([item]);
    render(<MaterialEnRevisionPage />, { wrapper: createQueryWrapper() });
    expect(hook.useMaterialEnRevision).toHaveBeenLastCalledWith({ soloPendientes: true, q: undefined });
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Todos' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Todos' }));
    expect(hook.useMaterialEnRevision).toHaveBeenLastCalledWith({ soloPendientes: false, q: undefined });
  });

  it('lista vacía muestra el estado vacío', () => {
    datos([]);
    render(<MaterialEnRevisionPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('No hay material en revisión')).toBeInTheDocument();
  });
});
