import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  AccionesComprobacion,
  type ComprobacionAccionable,
} from '@/features/cxp/components/AccionesComprobacion';
import {
  EstadoComprobacionGastos,
  TipoComprobacionGastos,
} from '@/features/cxp/api/types';

const toastMock = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('sonner', () => ({ toast: toastMock }));

const URL_APROBAR = '*/api/v1/cuentas-por-pagar/comprobaciones/c-1/aprobar-pasivo';

function comp(over: Partial<ComprobacionAccionable> = {}): ComprobacionAccionable {
  return {
    id: 'c-1',
    version: 3,
    estado: EstadoComprobacionGastos.PendienteAprobarPasivo,
    tipo: TipoComprobacionGastos.ReembolsoCajaChica,
    montoTotal: 150,
    moneda: 'MXN',
    ...over,
  };
}

function conPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  toastMock.success.mockClear();
  toastMock.error.mockClear();
  conPermisos(['cuentas_por_pagar.comprobaciones.aprobar-nivel1']);
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<AccionesComprobacion> — caja chica sin mínimo', () => {
  it('estado 7 con permiso: Aprobar pasivo y Rechazar, sin Autorizar', () => {
    render(<AccionesComprobacion c={comp()} />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('button', { name: 'Aprobar pasivo' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Rechazar/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Autorizar$/ })).not.toBeInTheDocument();
  });

  it('legada PorRevisar de caja chica también se aprueba como pasivo', () => {
    render(<AccionesComprobacion c={comp({ estado: EstadoComprobacionGastos.PorRevisar })} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('button', { name: 'Aprobar pasivo' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Autorizar$/ })).not.toBeInTheDocument();
  });

  it('sin aprobar-nivel1 no muestra acciones', () => {
    conPermisos([]);
    render(<AccionesComprobacion c={comp()} />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('aduanal PorRevisar conserva Firma N1 y no ofrece Aprobar pasivo', () => {
    render(
      <AccionesComprobacion
        c={comp({
          tipo: TipoComprobacionGastos.GastosAduanales,
          estado: EstadoComprobacionGastos.PorRevisar,
        })}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByRole('button', { name: /Firma N1/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aprobar pasivo' })).not.toBeInTheDocument();
  });

  it('Aplicada no muestra acciones', () => {
    render(<AccionesComprobacion c={comp({ estado: EstadoComprobacionGastos.Aplicada })} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('confirmar envía un POST a /aprobar-pasivo con X-Expected-Version e Idempotency-Key', async () => {
    const llamadas: Request[] = [];
    mswServer.use(
      http.post(URL_APROBAR, ({ request }) => {
        llamadas.push(request.clone());
        return HttpResponse.json({ id: 'c-1', estado: EstadoComprobacionGastos.Aplicada, version: 4 });
      }),
    );
    render(<AccionesComprobacion c={comp()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Aprobar pasivo' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Aprobar pasivo' }).at(-1)!);
    await waitFor(() => expect(llamadas).toHaveLength(1));
    expect(llamadas[0].headers.get('X-Expected-Version')).toBe('3');
    expect(llamadas[0].headers.get('Idempotency-Key')).toBeTruthy();
    await waitFor(() =>
      expect(toastMock.success).toHaveBeenCalledWith('Pasivo aprobado; reposición generada'),
    );
  });

  it('muestra el detalle del 422 cuando se excede el límite de aprobación', async () => {
    mswServer.use(
      http.post(URL_APROBAR, () =>
        HttpResponse.json(
          {
            title: 'Regla de negocio violada',
            status: 422,
            detail: 'El monto de la comprobación (150.00 MXN) excede tu límite de aprobación (100.00 MXN).',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<AccionesComprobacion c={comp()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Aprobar pasivo' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Aprobar pasivo' }).at(-1)!);
    await waitFor(() =>
      expect(toastMock.error).toHaveBeenCalledWith(
        expect.stringContaining('excede tu límite de aprobación'),
      ),
    );
  });
});
