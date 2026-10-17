import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ComparativoCotizaciones } from '@/features/compras/pages/ComparativoCotizaciones';
import {
  EstadoRequisicion,
  type ComparativoCotizaciones as Comparativo,
  type CotizacionComparativo,
} from '@/features/compras/api/types';

const hook = vi.hoisted(() => ({
  useComparativoCotizaciones: vi.fn(),
  useEliminarCotizacion: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
const permisos = vi.hoisted(() => ({ tiene: true }));

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permisos.tiene }));
vi.mock('@/features/compras/components/CotizacionSheet', () => ({
  CotizacionSheet: ({ abierto, cotizacion }: { abierto: boolean; cotizacion?: { proveedorRazonSocial: string } }) =>
    abierto && cotizacion ? <div role="dialog">Editando {cotizacion.proveedorRazonSocial}</div> : null,
}));

vi.mock('@/features/compras/api/useCotizaciones', () => hook);
vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'rq-1' }),
  useNavigate: () => vi.fn(),
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

beforeEach(() => {
  vi.clearAllMocks();
  permisos.tiene = true;
});

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

  it('con comparativo cerrado no ofrece editar ni eliminar', () => {
    datos({ comparativoCerrado: true, cotizaciones: [cot('1', 'Proveedor MXN')] });
    render(<ComparativoCotizaciones />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: /Acciones de la cotización/ })).not.toBeInTheDocument();
  });

  it('sin permiso de gestionar no ofrece editar ni eliminar', () => {
    permisos.tiene = false;
    datos({ cotizaciones: [cot('1', 'Proveedor MXN')] });
    render(<ComparativoCotizaciones />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: /Acciones de la cotización/ })).not.toBeInTheDocument();
  });

  it('con permiso y comparativo abierto, Editar abre el sheet con el proveedor precargado', async () => {
    datos({ cotizaciones: [cot('1', 'Proveedor MXN')] });
    render(<ComparativoCotizaciones />, { wrapper: createQueryWrapper() });
    const boton = screen.getByRole('button', { name: 'Acciones de la cotización de Proveedor MXN' });
    fireEvent.pointerDown(boton, { button: 0, ctrlKey: false, pointerType: 'mouse' });
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Editar' }));
    expect(await screen.findByText('Editando Proveedor MXN')).toBeInTheDocument();
  });

  it('sin motivo explica que falta; con cotización sin PDF avisa; vencida no se selecciona', () => {
    datos({
      cotizaciones: [
        cot('1', 'Proveedor MXN'),
        cot('2', 'Proveedor sin PDF', { archivoAdjuntoId: null, archivoNombre: null }),
        cot('3', 'Proveedor vencido', { vigente: false }),
      ],
    });
    render(<ComparativoCotizaciones />, { wrapper: createQueryWrapper() });
    const generar = screen.getByRole('button', { name: 'Generar OC con la elegida' });
    expect(generar).toBeDisabled();
    expect(screen.getByText('Falta: elegir una cotización')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('radio', { name: 'Elegir cotización de Proveedor sin PDF' }));
    expect(screen.getByText('Falta: PDF de la cotización')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('radio', { name: 'Elegir cotización de Proveedor MXN' }));
    expect(screen.getByText('Falta: motivo de la elección')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Motivo de la elección/), { target: { value: 'Menor precio y entrega' } });
    expect(generar).toBeEnabled();
    expect(screen.getByRole('radio', { name: 'Elegir cotización de Proveedor vencido' })).toBeDisabled();
  });
});
