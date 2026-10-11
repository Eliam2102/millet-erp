import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ReporteCarteraPage } from './ReporteCarteraPage';
vi.mock('@/features/cxp/api/useReportes', () => ({ useReporteCartera: () => ({ data: {
  titulo: 'Cartera', generadoEn: '2026-10-10T12:00:00Z', filtrosAplicados: [], totales: null,
  columnas: [{ key: 'en_revision', label: 'En revisión', tipo: 7 }], filas: [{ en_revision: false }, { en_revision: true }],
} }) }));
vi.mock('@/components/erp', async () => {
  const original = await vi.importActual<typeof import('@/components/erp')>('@/components/erp');
  return { ...original, ProveedorSelector: () => null, SucursalSelector: () => null };
});
describe('Reporte de cartera', () => {
  it('presenta Sí y No en la columna En revisión', () => {
    render(<ReporteCarteraPage />);
    expect(screen.getByRole('cell', { name: 'Sí' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'No' })).toBeInTheDocument();
    expect(screen.queryByText('false')).not.toBeInTheDocument();
  });
});
