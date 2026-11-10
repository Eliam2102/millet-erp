import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { TamboresLineaPage } from '@/features/almacen/pages/TamboresLineaPage';
import type { LineaTambores } from '@/features/almacen/api/useTamboresLinea';

const hook = vi.hoisted(() => ({ useTamboresLinea: vi.fn() }));
vi.mock('@/features/almacen/api/useTamboresLinea', () => ({ useTamboresLinea: hook.useTamboresLinea }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, params }: { children: ReactNode; params: { id: string } }) => <a href={`/almacen/salidas/${params.id}`}>{children}</a>,
}));

const tambor = {
  articuloId: 'art-1',
  articuloClave: 'SIL-A',
  articuloNombre: 'Silicón componente A',
  kgPresentacion: 200,
  movimientoId: 'mov-1',
};

const lineas: LineaTambores[] = [
  {
    id: 'l-1',
    sucursalId: 's-1',
    clave: 'INS1',
    nombre: 'Insulado 1',
    proceso: 'INSULADO',
    tambores: [
      { ...tambor, id: 't-2', estado: 'ACTIVO', fechaInicio: '2026-11-08T15:00:00Z', fechaFin: null, folio: 'M-SAL-2026-000002', folioReemplazo: null, movimientoId: 'mov-2' },
      { ...tambor, id: 't-1', estado: 'GASTADO', fechaInicio: '2026-11-06T15:00:00Z', fechaFin: '2026-11-08T15:00:00Z', folio: 'M-SAL-2026-000001', folioReemplazo: 'M-SAL-2026-000002' },
    ],
  },
  { id: 'l-2', sucursalId: 's-1', clave: 'INS2', nombre: 'Insulado 2', proceso: 'INSULADO', tambores: [] },
];

describe('TamboresLineaPage', () => {
  it('muestra por línea el tambor activo, el historial con folios y la línea sin tambores', () => {
    hook.useTamboresLinea.mockReturnValue({ data: lineas, isLoading: false, isError: false, refetch: vi.fn() });
    render(<TamboresLineaPage />, { wrapper: createQueryWrapper() });

    const ins1 = screen.getByRole('region', { name: /Insulado 1/ });
    expect(within(ins1).getByText('Activo')).toBeInTheDocument();
    expect(within(ins1).getByText('Gastado')).toBeInTheDocument();
    expect(within(ins1).getByRole('link', { name: 'M-SAL-2026-000001' })).toHaveAttribute('href', '/almacen/salidas/mov-1');
    expect(within(ins1).getAllByText('M-SAL-2026-000002')).toHaveLength(2); // folio del activo y reemplazo del gastado

    const ins2 = screen.getByRole('region', { name: /Insulado 2/ });
    expect(within(ins2).getByText('Sin tambor activo.')).toBeInTheDocument();
    expect(within(ins2).getByText('Aún no hay tambores')).toBeInTheDocument();
  });
});
