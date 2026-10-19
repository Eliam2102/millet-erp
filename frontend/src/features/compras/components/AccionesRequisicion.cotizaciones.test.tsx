import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createQueryWrapper } from '@/test/test-query-client';
import { AccionesRequisicion } from '@/features/compras/components/AccionesRequisicion';
import { EstadoRequisicion, type RequisicionResponse } from '@/features/compras/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  NuevaOrdenCompraContext,
  type NuevaOrdenCompraApi,
} from '@/features/compras/ordenes/components/nueva-orden-compra-context';

const comparativo = vi.hoisted(() => ({ cotizaciones: [] as unknown[] }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...rest }: { children: ReactNode; to: string }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock('@/lib/auth/useComprasSettings', () => ({ useAutoGenerarOcAlAutorizar: () => false }));
vi.mock('@/features/compras/api/useCotizaciones', () => ({
  useComparativoCotizaciones: () => ({ data: { cotizaciones: comparativo.cotizaciones } }),
}));

const nuevaOc: NuevaOrdenCompraApi = { abrir: () => {}, cerrar: () => {}, setDirty: () => {} };

function renderAcciones() {
  const QueryWrapper = createQueryWrapper();
  const rq = {
    id: 'rq-1',
    estado: EstadoRequisicion.EnSurtido,
    comprometidaEnOcId: null,
    sucursalId: 's-1',
    folio: 'MID2026-000042',
    lineas: [],
    autorizaciones: [],
  } as unknown as RequisicionResponse;
  render(<AccionesRequisicion rq={rq} />, {
    wrapper: ({ children }: { children: ReactNode }) => (
      <QueryWrapper>
        <NuevaOrdenCompraContext.Provider value={nuevaOc}>{children}</NuevaOrdenCompraContext.Provider>
      </QueryWrapper>
    ),
  });
}

function sesion() {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 't',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.ComprasOrdenesCrear, PermisosCanonicos.ComprasCotizacionesLeer],
    errorMessage: null,
  });
}

afterEach(() => {
  comparativo.cotizaciones = [];
  useAuthStore.setState({ status: 'idle', accessToken: null, user: null, permisos: [], currentEmpresaId: null });
});

describe('Convertir a OC con cotizaciones (D1, F1-COM-02)', () => {
  it('sin cotizaciones: conserva el botón Convertir a OC', () => {
    sesion();
    renderAcciones();
    expect(screen.getByRole('button', { name: /convertir a oc/i })).toBeInTheDocument();
  });

  it('con cotizaciones: lleva al comparativo y no ofrece precios estimados', () => {
    sesion();
    comparativo.cotizaciones = [{ id: 'c-1' }];
    renderAcciones();
    expect(screen.queryByRole('button', { name: /convertir a oc/i })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /elegir cotización y generar oc/i })).toHaveAttribute(
      'href',
      '/compras/requisiciones/$id/cotizaciones',
    );
  });
});
