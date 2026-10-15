import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { ConfirmDuplicarDialog } from '@/features/compras/ordenes/components/ConfirmDuplicarDialog';

/**
 * Verifica que el texto "Se copia / NO se copia" coincida con lo que hace
 * DuplicarOrdenCompraHandler: la información de importación se copia completa
 * (incluye BL, agente aduanal/naviera y ETA) y el pedimento NO se copia.
 */
describe('<ConfirmDuplicarDialog> — lista de copia de importación', () => {
  it('lista los campos de importación que sí se copian y excluye el pedimento', () => {
    render(
      <ConfirmDuplicarDialog
        open
        onOpenChange={vi.fn()}
        ocFolio="OC-X2026-000001"
        ocEstadoLabel="Cancelada"
        onConfirm={vi.fn()}
      />,
    );

    const copia = screen.getByRole('heading', { name: 'Se copia a la nueva OC' }).parentElement!;
    const noCopia = screen.getByRole('heading', { name: 'NO se copia' }).parentElement!;

    expect(within(copia).getByText(/Información de importación \(incoterm, país de origen, contenedor, ruta/))
      .toHaveTextContent('BL, tipo de transporte, agente aduanal/naviera, puerto de aduana, fecha estimada de arribo');
    expect(within(noCopia).getByText(/Número de pedimento/)).toBeInTheDocument();
    expect(within(copia).queryByText(/pedimento/i)).not.toBeInTheDocument();
  });
});
