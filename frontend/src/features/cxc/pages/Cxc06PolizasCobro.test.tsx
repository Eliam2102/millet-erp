import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  ConciliacionCobrosResponse,
  DepositoPorIdentificarItem,
  FacturaAbiertaItem,
  PropuestaAplicacionResponse,
} from '@/features/cxc/api/types';

// F1-CXC-02 · CXC-06 (86e3qddef): ajuste documentado de un cobro corto (tipo = cuenta y motivo), autorización por otra persona y
// conciliación de cobros contra pólizas.

const PROPONENTE = 'aaaaaaaa-0000-4000-8000-000000000001';
const AUTORIZADOR = 'bbbbbbbb-0000-4000-8000-000000000002';

const deposito: DepositoPorIdentificarItem = {
  movimientoBancarioId: 'mov-1',
  cuenta: 'BBVA ****6789',
  fechaValor: '2026-11-03',
  referencia: 'SPEI-USD',
  monto: 9_952,
  disponible: 9_952,
  moneda: 'USD',
  sucursalId: null,
  clienteId: '33333333-3333-4333-8333-333333333333',
  clienteNombre: 'ACME INC',
};

const factura: FacturaAbiertaItem = {
  facturaCarteraId: 'fc-1',
  uuid: 'u-1',
  folio: 'FV-10',
  moneda: 'USD',
  metodoPago: 'PUE',
  total: 10_000,
  montoPagado: 0,
  montoNc: 0,
  saldo: 10_000,
  fechaTimbrado: '2026-10-01',
  fechaVencimiento: '2026-10-31',
};

const mocks = vi.hoisted(() => ({
  crear: vi.fn(),
  autorizar: vi.fn(),
  rechazar: vi.fn(),
  propuesta: null as unknown,
  usuario: '' as string,
  conciliacion: null as unknown,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
  useNavigate: () => () => {},
  useSearch: () => ({}),
  useParams: () => ({ id: 'pap-1' }),
}));
vi.mock('@/features/cxc/components/ClienteSelectorCxc', () => ({
  ClienteSelectorCxc: ({ value }: { value: string | null }) => <span>cliente:{value}</span>,
}));
vi.mock('@/features/cxc/api/useLineasCredito', () => ({
  useClientesLookupCxc: () => ({ data: [{ id: 'cli-1', razonSocial: 'ACME INC' }] }),
}));
vi.mock('@/features/cxc/api/useAplicaciones', () => ({
  usePropuestaAplicacion: () => ({ data: mocks.propuesta, isLoading: false, isError: false }),
  useFacturasAbiertas: () => ({ data: [factura], isLoading: false, isError: false, error: null, refetch: () => {} }),
  useToleranciasNoFiscal: () => ({ data: { USD: 50 }, isSuccess: true, isError: false }),
  useCrearPropuestaAplicacion: () => ({ mutate: mocks.crear, isPending: false }),
  useAutorizarAjuste: () => ({ mutate: mocks.autorizar, isPending: false }),
  useRechazarAjuste: () => ({ mutate: mocks.rechazar, isPending: false }),
}));
vi.mock('@/features/cxc/api/useConciliacionPolizas', () => ({
  useConciliacionPolizas: () => ({ data: mocks.conciliacion, isLoading: false, isError: false }),
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/lib/auth/auth-store', () => ({
  useAuthStore: (sel: (s: { user: { id: string } | null; permisos: string[] }) => unknown) =>
    sel({ user: { id: mocks.usuario }, permisos: [] }),
}));
// Select nativo en jsdom (Radix no abre su menú sin eventos de puntero).
vi.mock('@/components/ui/select', () => ({
  Select: ({
    value,
    onValueChange,
    children,
  }: {
    value: string;
    onValueChange: (v: string) => void;
    children: React.ReactNode;
  }) => (
    <select aria-label="Tipo de ajuste" value={value} onChange={(e) => onValueChange(e.target.value)}>
      <option value="">Elige</option>
      {children}
    </select>
  ),
  SelectTrigger: () => null,
  SelectValue: () => null,
  SelectContent: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  SelectItem: ({ value, children }: { value: string; children: React.ReactNode }) => (
    <option value={value}>{children}</option>
  ),
}));

import { NuevaPropuestaAplicacion } from '@/features/cxc/pages/NuevaPropuestaAplicacion';
import { DetalleAplicacion } from '@/features/cxc/pages/DetalleAplicacion';
import { ConciliacionPolizasPage } from '@/features/cxc/pages/ConciliacionPolizasPage';

function propuestaConAjuste(extra: Partial<PropuestaAplicacionResponse> = {}): PropuestaAplicacionResponse {
  return {
    id: 'pap-1',
    clienteId: 'cli-1',
    depositoRef: 'SPEI-USD',
    montoDeposito: 9_952,
    moneda: 'USD',
    remittanceRef: 'REM-1',
    ajusteNoFiscal: -48,
    estado: 1,
    motivoRechazo: null,
    resueltaPor: null,
    resueltaEn: null,
    version: 3,
    propuestoPor: PROPONENTE,
    facturas: [{ facturaCarteraId: 'fc-1', facturaUuid: 'u-1', folio: 'FV-10', importeAplicado: 10_000, numParcialidad: null }],
    tipoAjuste: 1,
    motivoAjuste: 'Comisión del banco del cliente',
    ajusteAutorizadoPor: null,
    ajusteAutorizadoEn: null,
    ajustePorAutorizar: true,
    ...extra,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.usuario = AUTORIZADOR;
});

describe('CXC-06 · ajuste documentado de un cobro corto', () => {
  it('un depósito corto no se propone sin tipo y motivo; con ellos el comando los lleva', async () => {
    render(<NuevaPropuestaAplicacion onClose={() => {}} deposito={deposito} />);
    fireEvent.change(screen.getByLabelText(/Remittance del cliente/), { target: { value: 'REM-1' } });
    fireEvent.click(screen.getByLabelText('Aplicar a FV-10'));

    expect(screen.getByText(/no se absorbe solo/)).toBeInTheDocument();
    const proponer = screen.getByRole('button', { name: 'Proponer aplicación' });
    expect(proponer).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Tipo de ajuste'), { target: { value: '1' } });
    expect(proponer).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Motivo del ajuste/), { target: { value: 'Comisión del banco del cliente' } });
    expect(proponer).toBeEnabled();
    fireEvent.click(proponer);

    await waitFor(() => expect(mocks.crear).toHaveBeenCalled());
    const { command } = mocks.crear.mock.calls[0][0];
    expect(command.tipoAjuste).toBe(1);
    expect(command.motivoAjuste).toBe('Comisión del banco del cliente');
  });

  it('otra persona ve el ajuste por autorizar con importe, cuenta y motivo y lo autoriza', () => {
    mocks.propuesta = propuestaConAjuste();
    render(<DetalleAplicacion />);

    expect(screen.getAllByText('Ajuste por autorizar').length).toBeGreaterThan(0);
    expect(screen.getByText('Comisión bancaria')).toBeInTheDocument();
    expect(screen.getByText('Comisión del banco del cliente')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar ajuste' }));
    expect(mocks.autorizar).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'pap-1', versionEsperada: 3 }),
      expect.anything(),
    );
  });

  it('quien propuso no ve los botones para autorizar su propio ajuste', () => {
    mocks.usuario = PROPONENTE;
    mocks.propuesta = propuestaConAjuste();
    render(<DetalleAplicacion />);

    expect(screen.queryByRole('button', { name: 'Autorizar ajuste' })).not.toBeInTheDocument();
    expect(screen.getByText(/otra persona con permiso debe autorizar el ajuste/)).toBeInTheDocument();
  });

  it('rechazar el ajuste pide motivo', () => {
    mocks.propuesta = propuestaConAjuste();
    render(<DetalleAplicacion />);
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar ajuste' }));
    const confirmar = screen.getAllByRole('button', { name: 'Rechazar ajuste' }).at(-1)!;
    expect(confirmar).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Motivo del rechazo/), { target: { value: 'No es comisión' } });
    fireEvent.click(confirmar);
    expect(mocks.rechazar).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'pap-1', motivo: 'No es comisión' }),
      expect.anything(),
    );
  });

  it('con el ajuste rechazado el panel dice «Ajuste rechazado» con el motivo, no «Ajuste documentado»', () => {
    mocks.propuesta = propuestaConAjuste({
      estado: 3,
      ajustePorAutorizar: false,
      motivoRechazo: 'Ajuste rechazado: No es comisión',
      resueltaEn: '2026-11-04T18:00:00Z',
    });
    render(<DetalleAplicacion />);

    const panel = screen.getByRole('region', { name: 'Ajuste del cobro' });
    expect(within(panel).getByText('Ajuste rechazado')).toBeInTheDocument();
    expect(within(panel).queryByText('Ajuste documentado')).not.toBeInTheDocument();
    expect(within(panel).getByText(/^Rechazado el/)).toBeInTheDocument();
    expect(within(panel).getByText('Ajuste rechazado: No es comisión')).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Autorizar ajuste' })).not.toBeInTheDocument();
  });

  it('lista por omisión las diferencias con su causa probable', () => {
    const r: ConciliacionCobrosResponse = {
      desde: '2026-11-01',
      hasta: '2026-11-03',
      cobros: 2,
      cuadran: 1,
      conDiferencia: 1,
      enTransito: 0,
      filas: [
        {
          documento: 'Cobro de mostrador',
          documentoId: 'cob-1',
          clienteId: 'cli-1',
          cliente: 'ACME SA',
          moneda: 'MXN',
          fecha: '2026-11-03',
          importeCartera: 500,
          importePolizas: 0,
          diferencia: 500,
          polizas: [],
          estado: 2,
          causa: 'El cobro no tiene póliza: revisar la bandeja de rechazos del motor contable.',
        },
        {
          documento: 'REP',
          documentoId: 'mov-1',
          clienteId: 'cli-1',
          cliente: 'ACME SA',
          moneda: 'MXN',
          fecha: '2026-11-03',
          importeCartera: 2_320,
          importePolizas: 2_320,
          diferencia: 0,
          polizas: ['PR-000001'],
          estado: 1,
          causa: null,
        },
      ],
    };
    mocks.conciliacion = r;
    render(<ConciliacionPolizasPage />);

    expect(screen.getByText(/El cobro no tiene póliza/)).toBeInTheDocument();
    expect(screen.queryByText('PR-000001')).not.toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Cuadran/ }), { button: 0, ctrlKey: false });
    expect(screen.getByText('PR-000001')).toBeInTheDocument();
  });
});
