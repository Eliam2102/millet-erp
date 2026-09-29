import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteOrigenAwSection } from '@/modules/datos-maestros/components/ClienteOrigenAwSection';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ClienteDetalle } from '@/modules/datos-maestros/api/types';

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

function cliente(origenAw: Partial<NonNullable<ClienteDetalle['origenAw']>>) {
  return {
    id: 'c-1',
    clave: 'CLI-001',
    referenciaExterna: 'AW-777',
    razonSocial: 'Vidrios del Centro',
    codigoPostalFiscal: '76100',
    monedaDefault: 'MXN',
    origen: 1,
    origenAw: {
      condicionOrigen: 'N30',
      diasNominalesOrigen: 30,
      monedaOrigen: 'MXN',
      monedaNormalizada: 'MXN',
      nombreComercialOrigen: 'Vidrios del Centro',
      estadoOrigenCrudo: null,
      bloqueoOrigenCrudo: null,
      ultimaLecturaUtc: '2026-09-01T10:00:00Z',
      ultimaAplicacionUtc: '2026-09-01T10:00:00Z',
      resultado: 'Aplicado',
      error: null,
      versionContrato: 'v1',
      versionMapeo: 'm1',
      registroVersion: 1,
      ...origenAw,
    },
  } as unknown as ClienteDetalle;
}

afterEach(() => {
  useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [], user: null });
});

describe('<ClienteOrigenAwSection>', () => {
  it.each([['Error'], ['Pendiente'], ['Conflicto']] as const)(
    'con resultado %s nunca dice "Sincronizado"',
    (resultado) => {
      setPermisos([]);
      render(<ClienteOrigenAwSection cliente={cliente({ resultado })} />, {
        wrapper: createQueryWrapper(),
      });
      expect(screen.queryByText(/^sincronizado$/i)).not.toBeInTheDocument();
    },
  );

  it('Aplicado con aplicación real dice Sincronizado; sin aplicación no', () => {
    setPermisos([]);
    const { unmount } = render(<ClienteOrigenAwSection cliente={cliente({})} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('Sincronizado')).toBeInTheDocument();
    unmount();
    render(
      <ClienteOrigenAwSection
        cliente={cliente({ ultimaAplicacionUtc: '0001-01-01T00:00:00Z' })}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.queryByText('Sincronizado')).not.toBeInTheDocument();
  });

  it('la comparación muestra solo los campos que difieren', () => {
    setPermisos([]);
    render(
      <ClienteOrigenAwSection
        cliente={cliente({ nombreComercialOrigen: 'Vidrios Centro SA', monedaNormalizada: 'MXN' })}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('Razón social / nombre')).toBeInTheDocument();
    expect(screen.getByText('Vidrios Centro SA')).toBeInTheDocument();
    expect(screen.queryByRole('cell', { name: 'Moneda' })).not.toBeInTheDocument();
  });

  it('oculta Reintentar lectura sin sincronizar y lo muestra con el permiso', () => {
    setPermisos([]);
    const { unmount } = render(<ClienteOrigenAwSection cliente={cliente({})} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.queryByRole('button', { name: /reintentar lectura/i })).not.toBeInTheDocument();
    unmount();
    setPermisos([PermisosCanonicos.DatosMaestrosClientesSincronizar]);
    render(<ClienteOrigenAwSection cliente={cliente({})} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('button', { name: /reintentar lectura/i })).toBeInTheDocument();
  });

  it('un 503 no rompe la sección y el botón vuelve a estar disponible', async () => {
    setPermisos([PermisosCanonicos.DatosMaestrosClientesSincronizar]);
    mswServer.use(
      http.post('*/clientes/sincronizacion/reintentos', () =>
        HttpResponse.json(
          { type: 'x', title: 'Deshabilitada', status: 503, code: 'AW_CLIENTES_LECTURA_DESHABILITADA' },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<ClienteOrigenAwSection cliente={cliente({})} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /reintentar lectura/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /reintentar lectura/i })).toBeEnabled(),
    );
  });
});
