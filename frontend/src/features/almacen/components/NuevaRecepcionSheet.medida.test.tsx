import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevaRecepcionSheet } from '@/features/almacen/components/NuevaRecepcionSheet';

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('@/features/almacen/api', () => ({
  useRegistrarRecepcionFactura: () => ({ mutate: vi.fn(), isPending: false }),
  useRegistrarRecepcionPackingList: () => ({ mutate: vi.fn(), isPending: false }),
}));
vi.mock('@/features/almacen/api/useRecepciones', () => ({
  useCfdisParaRecepcion: () => ({ data: undefined }),
  useLineasRecibibles: () => ({
    data: [
      {
        lineaOcId: 'l-1', articuloId: 'a-1', unidadMedida: 'M2', cantidadSolicitada: 17.8608,
        cantidadRecibida: 0, pendiente: 17.8608, maximoRecibible: 17.8608, medida: null, requiereMedida: true,
      },
    ],
  }),
}));
vi.mock('@/features/compras/ordenes/api/useOrdenCompra', () => ({
  useOrdenCompra: () => ({
    data: {
      id: 'oc-1', esImportacion: false,
      lineas: [
        {
          id: 'l-1', posicion: 1, articuloId: 'a-1', articuloClave: 'VID-CLARO-6', articuloNombre: 'Vidrio',
          unidadMedida: 'M2', cantidad: 17.8608, cantidadRecibida: 0, esServicio: false,
        },
      ],
    },
  }),
}));

describe('<NuevaRecepcionSheet> — línea de vidrio sin medida en la OC', () => {
  it('avisa de entrada, deshabilita la fila y no habla de decimales', () => {
    render(<NuevaRecepcionSheet open onOpenChange={vi.fn()} ordenCompraIdInicial="oc-1" />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('La línea de la OC no tiene medida; corrígela en Compras.')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Incluir partida 1/ })).toBeDisabled();
    expect(screen.queryByText(/Demasiados decimales/)).not.toBeInTheDocument();
  });
});
