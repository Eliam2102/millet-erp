import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PanelFichasCobranza } from '@/features/cxc/components/PanelFichasCobranza';

const FICHAS = '*/api/v1/cuentas-por-cobrar/cobranza/fichas';

const fila = (extra: Record<string, unknown>) => ({
  facturaCarteraId: 'f-1',
  clienteId: 'c-1',
  clienteNombre: 'Cliente DEMO',
  folio: 'FV-1',
  moneda: 'MXN',
  saldo: 1000,
  fechaVencimiento: '2026-10-01T00:00:00Z',
  diasVencida: 40,
  fichaId: null,
  responsableId: null,
  responsableNombre: null,
  siguienteRevision: null,
  escalada: false,
  revisionVencida: false,
  cerrada: false,
  ...extra,
});

describe('PanelFichasCobranza (M-241)', () => {
  beforeEach(() => {
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
      empresas: [],
      currentEmpresaId: 'e-test',
      permisos: ['cuentas_por_cobrar.cartera.leer', 'cuentas_por_cobrar.cobranza.registrar'],
      errorMessage: null,
    });
  });

  it('marca «Sin responsable» en la factura vencida sin ficha y muestra la escalada con su responsable', async () => {
    mswServer.use(
      http.get(FICHAS, () =>
        HttpResponse.json([
          fila({}),
          fila({ facturaCarteraId: 'f-2', folio: 'FV-2', fichaId: 'x', responsableId: 'u-9', responsableNombre: 'Supervisor DEMO', escalada: true }),
        ]),
      ),
    );
    render(<PanelFichasCobranza clienteId={null} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Sin responsable')).toBeTruthy();
    expect(await screen.findByText('Supervisor DEMO')).toBeTruthy();
    expect(screen.getByText('Escalada')).toBeTruthy();
    expect(screen.getAllByRole('button', { name: 'Gestión' })).toHaveLength(2);
    // Reprogramar y escalar solo existen cuando ya hay ficha.
    expect(screen.getAllByRole('button', { name: 'Escalar' })).toHaveLength(1);
  });
});
