import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Anticipos } from './FinanzasPanel';
import type { Anticipo, Finanzas } from '../api/finanzas';
import type { ExpedienteDetalle } from '../api/types';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

const mocks = vi.hoisted(() => ({
  mutateAsync: vi.fn().mockResolvedValue({}),
  permisos: new Set<string>(),
}));
vi.mock('@/lib/auth/useHasPermission', () => ({
  useHasPermission: (p: string) => mocks.permisos.has(p),
}));
vi.mock('../api/finanzas', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/finanzas')>()),
  useMutarFinanzas: () => ({ mutateAsync: mocks.mutateAsync, isPending: false, isError: false }),
}));

const data = {
  expediente: { id: 'e', estado: 1 },
  participantes: [],
} as unknown as ExpedienteDetalle;
const comprobado = {
  id: 'a',
  agenteId: 'agente',
  pedimento: '26 24 3984 6001234',
  anticipado: 50000,
  aplicado: 42000,
  diferencia: 8000,
  saldo: 8000,
  estado: 'Saldo a favor abierto',
  compensacionRecibida: 0,
  resoluciones: [],
} as Anticipo;
const finanzas = (anticipos: Anticipo[]) =>
  ({
    anticipos,
    pendientes: [],
    nombres: { agente: 'Agencia Aduanal del Golfo', ana: 'Ana Ruiz', luis: 'Luis Contreras' },
    autorizaciones: [],
  }) as unknown as Finanzas;

describe('M-327 reversa de un anticipo ya comprobado', () => {
  beforeEach(() => {
    mocks.mutateAsync.mockClear();
    mocks.permisos.clear();
  });

  it('Comercio exterior solicita la reversa sólo con motivo', async () => {
    mocks.permisos.add(P.ComercioExteriorAnticiposRevertirComprobacionSolicitar);
    render(<Anticipos data={data} finanzas={finanzas([comprobado])} />);
    const boton = screen.getByRole('button', { name: 'Revertir comprobación' });
    expect(boton).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Autorizar reversa' })).not.toBeInTheDocument();
    fireEvent.change(
      screen.getByLabelText(
        'Motivo para revertir una comprobación (la autoriza el Contador General)',
      ),
      { target: { value: 'Facturas del agente mal aplicadas' } },
    );
    fireEvent.click(boton);
    await waitFor(() =>
      expect(mocks.mutateAsync).toHaveBeenCalledWith({
        ruta: 'anticipos/a/revertir-comprobacion',
        body: { motivo: 'Facturas del agente mal aplicadas' },
      }),
    );
  });

  it('el Contador General autoriza o no autoriza con motivo la reversa pendiente', async () => {
    mocks.permisos.add(P.ComercioExteriorAnticiposRevertirComprobacionAutorizar);
    const pendiente = {
      ...comprobado,
      estado: 'Reversa de comprobación por autorizar',
      reversaPendiente: {
        id: 's',
        motivo: 'Facturas del agente mal aplicadas',
        solicitadoPor: 'ana',
        solicitadoEn: '2026-11-10T16:00:00Z',
      },
    };
    render(<Anticipos data={data} finanzas={finanzas([pendiente])} />);
    expect(screen.getByText(/Reversa solicitada por Ana Ruiz/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revertir comprobación' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'No autorizar' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar reversa' }));
    await waitFor(() =>
      expect(mocks.mutateAsync).toHaveBeenCalledWith({
        ruta: 'anticipos/a/reversa/autorizar',
        body: {},
      }),
    );
    fireEvent.change(screen.getByLabelText('Motivo para no autorizar una reversa'), {
      target: { value: 'La comprobación es correcta' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'No autorizar' }));
    await waitFor(() =>
      expect(mocks.mutateAsync).toHaveBeenCalledWith({
        ruta: 'anticipos/a/reversa/rechazar',
        body: { motivo: 'La comprobación es correcta' },
      }),
    );
  });

  it('muestra el saldo a favor y el historial con usuario, motivo y firma', () => {
    const revertido = {
      ...comprobado,
      aplicado: null,
      diferencia: null,
      saldo: null,
      estado: 'Pendiente de comprobar',
      comprobacionRevertida: true,
      saldoAFavorAgenteMxn: 50000,
      reversasComprobacion: [
        {
          solicitudId: 's',
          motivo: 'Facturas del agente mal aplicadas',
          solicitadoPor: 'ana',
          solicitadoEn: '2026-11-10T16:00:00Z',
          autorizada: true,
          firmadoPor: 'luis',
          firmadoEn: '2026-11-10T17:00:00Z',
          motivoRechazo: null,
          saldoAFavorMxn: 50000,
          gastosLiberadosMxn: 42000,
        },
      ],
    };
    render(<Anticipos data={data} finanzas={finanzas([revertido])} />);
    expect(screen.getByText('Pendiente de comprobar')).toBeInTheDocument();
    expect(screen.getByText(/Saldo a favor del agente: \$50,000/)).toBeInTheDocument();
    const historial = screen.getByText(/Solicitó Ana Ruiz/);
    expect(historial).toHaveTextContent('Facturas del agente mal aplicadas');
    expect(historial).toHaveTextContent('Autorizó Luis Contreras');
  });
});
