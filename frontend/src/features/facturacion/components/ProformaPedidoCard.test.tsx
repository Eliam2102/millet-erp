import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { ProformaPedidoCard } from './ProformaPedidoCard';

const BASE = {
  id: 'pf-2',
  pedidoFacturableId: 'p-1',
  folio: 'PF-MID-001-2',
  version: 2,
  generadaEn: '2026-11-10T15:30:00Z',
  generadaPor: 'Cajera Prueba',
  numeroPedido: 'MID-001',
  avisos: [],
  vigenteHasta: '2026-11-25',
  vencida: false,
  conformidad: null,
  contenido: { moneda: 'MXN', totales: { total: 116 } },
} as unknown as ProformaPedidoResponse;

function renderCard(proforma: ProformaPedidoResponse, onConformidad = vi.fn()) {
  render(
    <ProformaPedidoCard
      proforma={proforma}
      cargando={false}
      descargando={false}
      onVer={vi.fn()}
      onDescargar={vi.fn()}
      onConformidad={onConformidad}
    />,
  );
  return onConformidad;
}

describe('ProformaPedidoCard · conformidad (M-234)', () => {
  it('muestra la vigencia y ofrece registrar la conformidad de la versión', () => {
    const onConformidad = renderCard(BASE);
    expect(screen.getByText(/Vigente hasta el 25\/11\/2026/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Registrar conformidad de la proforma PF-MID-001-2/ }));
    expect(onConformidad).toHaveBeenCalledWith(BASE);
  });

  it('una proforma vencida no ofrece registrar conformidad', () => {
    renderCard({ ...BASE, vencida: true });
    expect(screen.getByText(/Vencida/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Registrar conformidad/ })).not.toBeInTheDocument();
  });

  it('con conformidad muestra quién, cuándo y medio y oculta la acción', () => {
    renderCard({
      ...BASE,
      conformidad: {
        id: 'c-1',
        proformaId: 'pf-2',
        quien: 'Contacto Prueba',
        fecha: '2026-11-11',
        medio: 'Telefono',
        notas: null,
        registradaPor: 'Cajera Prueba',
        registradaEn: '2026-11-11T16:00:00Z',
      },
    });
    expect(screen.getByText('Conforme: Contacto Prueba · 11/11/2026 · Teléfono')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Registrar conformidad/ })).not.toBeInTheDocument();
  });
});
