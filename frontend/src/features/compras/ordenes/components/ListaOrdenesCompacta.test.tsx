import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ListaOrdenesCompacta } from '@/features/compras/ordenes/components/ListaOrdenesCompacta';
import type { OrdenCompraResumen } from '@/features/compras/ordenes/api/types';
import type { BandejaOcSearch } from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <a href="#" className={className}>
      {children}
    </a>
  ),
}));

function renderLista(esRecurrente: boolean | null | undefined) {
  const item = {
    id: 'oc-1',
    folio: 'OC2026-000001',
    estado: 0,
    proveedorId: 'p-1',
    proveedorNombre: 'Proveedor Uno',
    fechaDocumento: '2026-05-09',
    esRecurrente,
  } as unknown as OrdenCompraResumen;
  render(<ListaOrdenesCompacta items={[item]} idActivo={null} search={{} as BandejaOcSearch} />);
}

describe('<ListaOrdenesCompacta /> badge de recurrencia', () => {
  it('con null no pinta ningún badge de recurrencia', () => {
    renderLista(null);
    expect(screen.queryByText('Recurrente')).toBeNull();
    expect(screen.queryByText('No recurrente')).toBeNull();
  });

  it('con true pinta "Recurrente"', () => {
    renderLista(true);
    expect(screen.getByText('Recurrente')).toBeInTheDocument();
  });

  it('con false pinta "No recurrente"', () => {
    renderLista(false);
    expect(screen.getByText('No recurrente')).toBeInTheDocument();
  });
});
