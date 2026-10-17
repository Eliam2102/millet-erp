import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleDevolucion } from './DetalleDevolucion';

let permisos: string[] = [];
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  Link: ({ children }: { children: React.ReactNode }) => <a href="#">{children}</a>,
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: (p: string) => permisos.includes(p) }));

const base = {
  id: 'dev-12345678', origen: 1, estado: 1, clienteId: 'cli-1', clienteClave: 'C1', clienteRazonSocial: 'Cliente SA',
  importe: 500, importeMaximoOrigen: 1000, disponibleOrigen: 1000, moneda: 'MXN', motivo: 'Duplicado',
  movimientoOrigenId: 'mov-1', notaCreditoId: null, notaCreditoFolio: null, notaCreditoUuid: null,
  destinoBanco: 'BBVA', destinoClabe: '**************9719', destinoTitular: 'Cliente SA',
  solicitadaPor: 'u1', solicitadaEn: '2026-10-17T12:00:00Z', autorizadaPor: null, autorizadaEn: null,
  motivoRechazo: null, motivoCancelacion: null, movimientoPagoId: null, pagadaEn: null, sucursalId: null, version: 1,
};

function detalle(o: object = {}) {
  mswServer.use(http.get('*/api/v1/tesoreria/devoluciones/dev-12345678', () => HttpResponse.json({ ...base, ...o })));
  render(<DetalleDevolucion id="dev-12345678" />, { wrapper: createQueryWrapper() });
}

describe('<DetalleDevolucion> acciones', () => {
  beforeEach(() => {
    permisos = [];
  });

  it('Autorizar solo con permiso y en estado Solicitada', async () => {
    detalle();
    await screen.findByText('Cuenta destino');
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });

  it('con permiso en estado Solicitada lo muestra', async () => {
    permisos = ['tesoreria.devoluciones.autorizar'];
    detalle();
    expect(await screen.findByRole('button', { name: 'Autorizar' })).toBeInTheDocument();
  });

  it('en estado Autorizada no ofrece Autorizar', async () => {
    permisos = ['tesoreria.devoluciones.autorizar'];
    detalle({ estado: 2 });
    await screen.findByText('Cuenta destino');
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });

  it('muestra el detail del 422 DEV_MISMO_USUARIO', async () => {
    permisos = ['tesoreria.devoluciones.autorizar'];
    mswServer.use(
      http.post('*/api/v1/tesoreria/devoluciones/dev-12345678/autorizar', () =>
        HttpResponse.json(
          { title: 'Regla de negocio', status: 422, code: 'DEV_MISMO_USUARIO', detail: 'Quien solicita la devolución no puede autorizarla. Pide la autorización al Gerente o al Jefe de Tesorería.' },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        )),
    );
    detalle();
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar devolución' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/no puede autorizarla/));
  });
});
