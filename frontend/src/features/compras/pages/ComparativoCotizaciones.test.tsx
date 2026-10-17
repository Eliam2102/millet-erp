import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ComparativoCotizaciones } from '@/features/compras/pages/ComparativoCotizaciones';
import {
  EstadoRequisicion,
  type ComparativoCotizaciones as Comparativo,
  type CotizacionComparativo,
} from '@/features/compras/api/types';

const hook = vi.hoisted(() => ({ useComparativoCotizaciones: vi.fn() }));

vi.mock('@/features/compras/api/useCotizaciones', () => hook);
vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'rq-1' }),
  Link: ({ children }: { children: React.ReactNode }) => <a href="#">{children}</a>,
}));

const LINEA = 'l-1';

function cot(
  id: string,
  proveedor: string,
  over: Partial<CotizacionComparativo> = {},
  esMenorPrecio = false,
  precioMxn: number | null = 500,
): CotizacionComparativo {
  return {
    id,
    proveedorId: `p-${id}`,
    proveedorClave: null,
    proveedorRazonSocial: proveedor,
    proveedorRfc: null,
    proveedorActivo: true,
    folioProveedor: `COT-${id}`,
    moneda: 'MXN',
    fechaCotizacion: '2026-10-15',
    vigenteHasta: '2026-10-31',
    vigente: true,
    condicionesCredito: '30 días',
    archivoAdjuntoId: 'a-1',
    archivoNombre: 'cot.pdf',
    elegida: false,
    motivoEleccion: null,
    elegidaPor: null,
    elegidaEn: null,
    ordenCompraId: null,
    registradaPor: 'u-1',
    registradaEn: '2026-10-15T10:00:00Z',
    version: 1,
    lineas: [
      {
        lineaRequisicionId: LINEA,
        unidadCotizada: 'HOJA',
        factorAUnidadRq: 1,
        precioUnitario: 500,
        diasEntrega: 3,
        precioPorUnidadRq: 500,
        precioMxnPorUnidadRq: precioMxn,
        importeMxn: precioMxn === null ? null : precioMxn * 4,
        esMenorPrecio,
      },
    ],
    ...over,
  };
}

function datos(over: Partial<Comparativo> = {}) {
  const data: Comparativo = {
    requisicionId: 'rq-1',
    requisicionFolio: 'MID2026-010009',
    sucursalId: 's-1',
    estadoRequisicion: EstadoRequisicion.EnSurtido,
    comprometidaEnOcId: null,
    puedeRegistrar: true,
    comparativoCerrado: false,
    hoy: '2026-10-17',
    partidas: [
      {
        lineaRequisicionId: LINEA,
        posicion: 1,
        articuloId: 'art-1',
        articuloClave: 'SIL-01',
        articuloNombre: 'Silicón estructural',
        cantidadDeCompra: 4,
        saldoPorComprar: 4,
        unidad: 'HOJA',
      },
    ],
    tiposCambio: [],
    cotizaciones: [],
    ...over,
  };
  hook.useComparativoCotizaciones.mockReturnValue({ data, isLoading: false, isError: false, refetch: vi.fn() });
}

beforeEach(() => vi.clearAllMocks());

describe('ComparativoCotizaciones', () => {
  it('marca el menor precio, señala la vencida y deshabilita su radio', () => {
    datos({
      cotizaciones: [
        cot('1', 'Proveedor MXN', {}, false, 500),
        cot('2', 'Proveedor USD', { moneda: 'USD' }, true, 446.25),
        cot('3', 'Proveedor vencido', { vigente: false, vigenteHasta: '2026-10-10' }, false, 520),
      ],
    });
    render(<ComparativoCotizaciones />, { wrapper: createQueryWrapper() });
    expect(screen.getAllByText('Más bajo')).toHaveLength(1);
    expect(screen.getByText(/Vencida 2026-10-10/)).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Elegir cotización de Proveedor vencido' })).toBeDisabled();
    expect(screen.getByRole('radio', { name: 'Elegir cotización de Proveedor MXN' })).toBeEnabled();
  });

  it('avisa cuando falta el tipo de cambio', () => {
    datos({
      tiposCambio: [{ moneda: 'USD', fecha: '2026-10-17', valorEnMxn: null }],
      cotizaciones: [cot('2', 'Proveedor USD', { moneda: 'USD' }, false, null)],
    });
    render(<ComparativoCotizaciones />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/Sin tipo de cambio de USD/)).toBeInTheDocument();
    expect(screen.getByText('Sin TC')).toBeInTheDocument();
  });

  it('muestra el estado vacío sin cotizaciones', () => {
    datos();
    render(<ComparativoCotizaciones />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('Aún no hay cotizaciones registradas')).toBeInTheDocument();
  });
});
