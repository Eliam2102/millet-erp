import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ActasCierreSection } from '@/features/almacen/components/ActasCierreSection';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { ActaCierre } from '@/features/almacen/api/useAvisosAlmacen';

const m = vi.hoisted(() => ({ actas: [] as unknown[], revisar: vi.fn() }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/features/almacen/api/useAvisosAlmacen', () => ({
  useActasCierre: () => ({ data: m.actas, isError: false }),
  useRevisarActaCierre: () => ({ mutate: m.revisar, isPending: false }),
}));

const acta = (extra: Partial<ActaCierre> = {}): ActaCierre => ({
  id: 'a-1', anio: 2026, mes: 10, estado: 'POR_REVISAR',
  ejecutadoPorId: 'u-1', ejecutadoPor: 'Contador General', ejecutadoAt: '2026-11-03T16:00:00Z',
  revisadaPorId: null, revisadaPor: null, revisadaAt: null,
  contenido: {
    almacenes: [{ almacen: 'Almacén Mérida', renglones: 42, valorInventarioMxn: 1850000 }],
    conteos: [{ folio: 'CNT-1A2B3C4D', tipo: 'Rotativo', fechaAprobacion: '2026-10-20T15:00:00Z' }],
    vales: [{ folio: 'M-VAL-000012', fecha: '2026-10-14' }],
  },
  ...extra,
});

const conPermisos = (permisos: string[]) => useAuthStore.setState({ permisos });

describe('ActasCierreSection', () => {
  beforeEach(() => { vi.clearAllMocks(); m.actas = [acta()]; });
  afterEach(() => conPermisos([]));

  it('muestra el acta por revisar con sus totales y deja marcarla a quien tiene el permiso', () => {
    conPermisos(['almacen.cierre.revisar-acta']);
    render(<ActasCierreSection />);
    expect(screen.getByText('Por revisar')).toBeInTheDocument();
    expect(screen.getByText('Almacén Mérida')).toBeInTheDocument();
    expect(screen.getByText(/CNT-1A2B3C4D/)).toBeInTheDocument();
    expect(screen.getByText(/M-VAL-000012/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Marcar revisada' }));
    expect(m.revisar).toHaveBeenCalledWith('a-1', expect.anything());
  });

  it('sin el permiso no ofrece marcar revisada', () => {
    conPermisos(['almacen.cierre-mes.ejecutar']);
    render(<ActasCierreSection />);
    expect(screen.queryByRole('button', { name: 'Marcar revisada' })).toBeNull();
    expect(screen.getByText('La revisa la Dirección de Administración.')).toBeInTheDocument();
  });

  it('un acta revisada muestra quién y cuándo, sin botón', () => {
    conPermisos(['almacen.cierre.revisar-acta']);
    m.actas = [acta({ estado: 'REVISADA', revisadaPor: 'Directora de Administración', revisadaAt: '2026-11-04T15:00:00Z' })];
    render(<ActasCierreSection />);
    expect(screen.getByText('Revisada')).toBeInTheDocument();
    expect(screen.getByText(/Revisada por Directora de Administración/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Marcar revisada' })).toBeNull();
  });
});
