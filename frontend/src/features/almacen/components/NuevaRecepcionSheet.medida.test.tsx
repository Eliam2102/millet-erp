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
      {
        lineaOcId: 'l-2', articuloId: 'a-2', unidadMedida: 'M2', cantidadSolicitada: 178.608,
        cantidadRecibida: 0, pendiente: 178.608, maximoRecibible: 178.608, requiereMedida: false,
        medida: { varianteId: 'v-1', anchoMm: 2440, altoMm: 3660, areaHojaM2: 8.9304 },
      },
      {
        lineaOcId: 'l-3', articuloId: 'a-3', unidadMedida: 'PZA', cantidadSolicitada: 5,
        cantidadRecibida: 0, pendiente: 5, maximoRecibible: 5, medida: null, requiereMedida: false,
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
        {
          id: 'l-2', posicion: 2, articuloId: 'a-2', articuloClave: 'VID-CLARO-6B', articuloNombre: 'Vidrio con medida',
          unidadMedida: 'M2', cantidad: 178.608, cantidadRecibida: 0, esServicio: false,
        },
        {
          id: 'l-3', posicion: 3, articuloId: 'a-3', articuloClave: 'INS-1', articuloNombre: 'Insumo',
          unidadMedida: 'PZA', cantidad: 5, cantidadRecibida: 0, esServicio: false,
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

  it('pide el lote del proveedor en cada fila: obligatorio con medida, opcional sin ella', () => {
    render(<NuevaRecepcionSheet open onOpenChange={vi.fn()} ordenCompraIdInicial="oc-1" />, {
      wrapper: createQueryWrapper(),
    });
    const conMedida = screen.getByLabelText('Lote del proveedor, partida 2');
    expect(conMedida).toHaveAttribute('placeholder', 'Lote del proveedor');
    expect(conMedida).toHaveAttribute('maxlength', '50');
    expect(screen.getByLabelText('Lote del proveedor, partida 3')).toHaveAttribute('placeholder', 'Lote (opcional)');
  });
});
