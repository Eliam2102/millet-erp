import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ExpedienteDetalle } from '../api/types';
import { FinanzasPanel } from './FinanzasPanel';
const { guardar } = vi.hoisted(() => ({ guardar: vi.fn().mockResolvedValue({}) }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('./ReferenciaSelector', () => ({
  ReferenciaSelector: ({ onChange }: { onChange: (id: string) => void }) => (
    <button type="button" onClick={() => onChange('proveedor')}>
      Elegir proveedor
    </button>
  ),
}));
vi.mock('../api/hooks', () => ({
  CE_BASE: '/ce',
  useContenedores: () => ({ data: { data: { contenedores: [] } } }),
  useExpedientes: () => ({}),
  useMutarContenedor: () => ({}),
}));
vi.mock('@tanstack/react-query', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tanstack/react-query')>()),
  useMutation: () => ({}),
}));
vi.mock('../api/finanzas', () => ({
  useFinanzas: () => ({
    data: {
      data: {
        pendientes: [],
        estadoFinanciero: 'Abierto',
        nombres: {},
        conceptos: [
          {
            id: 'hon',
            nombre: 'Honorarios',
            baseImportacion: 1,
            baseExportacion: 2,
            requiereOrdenServicio: true,
          },
        ],
        gastos: [],
        partidas: [],
        bases: [],
        distribuciones: [],
        anticipos: [],
        ordenesServicio: [
          {
            id: 'oc',
            folio: 'OC-P6',
            moneda: 'MXN',
            total: 50000,
            asignado: 50000,
            facturado: 52000,
            diferencia: 2000,
          },
        ],
      },
      etag: '"1"',
    },
  }),
  useMutarFinanzas: () => ({ mutateAsync: guardar }),
  useOrdenesServicio: () => ({
    data: {
      data: [{ id: 'oc', folio: 'OC-P6', moneda: 'MXN', total: 50000, tieneCotizacion: true }],
    },
  }),
}));
const expediente = {
  expediente: { id: 'e', estado: 1, tipo: 1 },
  participantes: [],
} as unknown as ExpedienteDetalle;
beforeEach(() => guardar.mockClear());
describe('Órdenes de servicio en gastos', () => {
  it('muestra el total, las facturas agrupadas y el excedente', () => {
    render(<FinanzasPanel data={expediente} tab="gastos" />);
    expect(
      screen.getByRole('region', { name: 'Órdenes de servicio del expediente' }),
    ).toBeInTheDocument();
    expect(screen.getByText('$52,000.00')).toBeInTheDocument();
    expect(screen.getByText('$2,000.00')).toBeInTheDocument();
  });
  it('exige la orden, llena moneda y cotizado y limpia la selección al cambiar proveedor', () => {
    render(<FinanzasPanel data={expediente} tab="gastos" />);
    fireEvent.click(screen.getByRole('button', { name: 'Registrar gasto' }));
    fireEvent.click(screen.getByRole('button', { name: 'Elegir proveedor' }));
    expect(screen.getByRole('button', { name: 'Guardar gasto' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Orden de servicio *'), { target: { value: 'oc' } });
    expect(screen.getByLabelText('Cotizado de la orden (MXN)')).toHaveValue(50000);
    expect(screen.getByLabelText('Cotizado de la orden (MXN)')).toHaveAttribute('readonly');
    expect(screen.getByRole('button', { name: 'Guardar gasto' })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Elegir proveedor' }));
    expect(screen.getByLabelText('Orden de servicio *')).toHaveValue('');
  });
  it('pide fuente, tipo de cambio y nota (mín. 10) al cambiar el tipo de cambio en USD', () => {
    render(<FinanzasPanel data={expediente} tab="gastos" />);
    fireEvent.click(screen.getByRole('button', { name: 'Registrar gasto' }));
    expect(screen.queryByLabelText('Fuente del tipo de cambio')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Moneda *'), { target: { value: 'USD' } });
    fireEvent.change(screen.getByLabelText('Fuente del tipo de cambio'), {
      target: { value: 'Negociado' },
    });
    expect(screen.getByLabelText('Tipo de cambio usado *')).toBeRequired();
    expect(screen.getByLabelText('Nota del cambio (mín. 10 caracteres) *')).toHaveAttribute(
      'minlength',
      '10',
    );
    fireEvent.change(screen.getByLabelText('Fuente del tipo de cambio'), {
      target: { value: 'CFDI' },
    });
    expect(screen.getByLabelText('Tipo de cambio usado')).not.toBeRequired();
  });
});
