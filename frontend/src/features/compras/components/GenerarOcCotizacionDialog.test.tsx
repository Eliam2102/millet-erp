import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { GenerarOcCotizacionDialog } from '@/features/compras/components/GenerarOcCotizacionDialog';
import type { ComparativoCotizaciones, CotizacionComparativo } from '@/features/compras/api/types';

const mocks = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  navigate: vi.fn(),
}));

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => mocks.navigate }));
vi.mock('@/features/compras/ordenes/api/useCrearOrdenCompraDesdeRequisicion', () => ({
  useCrearOrdenCompraDesdeRequisicion: () => ({ mutateAsync: mocks.mutateAsync, isPending: false }),
}));
vi.mock('@/features/compras/ordenes/api/useSucursalesPermitidas', () => ({
  useSucursalesPermitidas: () => ({ data: [{ id: 's-1', clave: 'MID', nombre: 'Mérida' }] }),
}));
vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  CondicionesPagoSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('cp-1')}>
      elegir condiciones
    </button>
  ),
  UsoPrincipalSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('uso-1')}>
      elegir uso
    </button>
  ),
}));

const cotizacion = {
  id: 'cot-usd',
  proveedorId: 'prov-1',
  proveedorRazonSocial: 'Proveedor USD',
  moneda: 'USD',
} as unknown as CotizacionComparativo;

const data = {
  requisicionId: 'rq-1',
  sucursalId: 's-1',
  tiposCambio: [{ moneda: 'USD', fecha: '2026-10-17', valorEnMxn: 17.85 }],
} as unknown as ComparativoCotizaciones;

beforeEach(() => {
  vi.clearAllMocks();
  mocks.mutateAsync.mockResolvedValue({ ordenCompraId: 'oc-1', folio: 'OC-MID2026-000001' });
});

describe('GenerarOcCotizacionDialog', () => {
  it('envía cotizacionId, motivo, moneda, TC prellenado y proveedor de la cotización', async () => {
    const onClose = vi.fn();
    render(
      <GenerarOcCotizacionDialog
        abierto
        onClose={onClose}
        data={data}
        cotizacion={cotizacion}
        motivoEleccion="Menor precio y entrega"
      />,
    );
    const generar = screen.getByRole('button', { name: 'Generar orden de compra' });
    expect(generar).toBeDisabled();
    expect(screen.getByText('Falta: condiciones de pago')).toBeInTheDocument();
    expect(screen.getByLabelText(/Tipo de cambio/)).toHaveValue(17.85);

    fireEvent.click(screen.getByRole('button', { name: 'elegir condiciones' }));
    fireEvent.click(screen.getByRole('button', { name: 'elegir uso' }));
    fireEvent.click(generar);

    await waitFor(() => expect(mocks.mutateAsync).toHaveBeenCalledTimes(1));
    const { command } = mocks.mutateAsync.mock.calls[0]![0];
    expect(command).toMatchObject({
      requisicionId: 'rq-1',
      sucursalCodigo: 'MID',
      proveedorId: 'prov-1',
      moneda: 'USD',
      tipoCambio: 17.85,
      cotizacionId: 'cot-usd',
      motivoEleccion: 'Menor precio y entrega',
      condicionesPagoId: 'cp-1',
      usoPrincipalId: 'uso-1',
    });
    await waitFor(() =>
      expect(mocks.navigate).toHaveBeenCalledWith({ to: '/compras/ordenes/$id', params: { id: 'oc-1' } }),
    );
  });
});

 it('hereda pago y entrega de la cotización y permite editar la entrega', async () => {
    render(<GenerarOcCotizacionDialog abierto onClose={vi.fn()} data={data}
      cotizacion={{ ...cotizacion, condicionesPagoId: 'cp-demo', fechaEntrega: '2026-11-10' }}
      motivoEleccion="DEMO ficticio: entrega" />);
    expect(screen.getByLabelText(/Fecha de entrega esperada/)).toHaveValue('2026-11-10');
    fireEvent.click(screen.getByRole('button', { name: 'elegir uso' }));
    fireEvent.change(screen.getByLabelText(/Fecha de entrega esperada/), { target: { value: '2026-11-12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generar orden de compra' }));
    await waitFor(() => expect(mocks.mutateAsync).toHaveBeenCalled());
    expect(mocks.mutateAsync.mock.calls[0]![0].command).toMatchObject({
      condicionesPagoId: 'cp-demo', fechaEntregaEsperada: '2026-11-12',
    });
 });
