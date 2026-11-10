import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { NotasCreditoPorAutorizar } from './NotasCreditoPorAutorizar';

const nc = {
  id: '00000000-0000-0000-0000-0000000000aa',
  folio: 'NC-000001',
  estado: 'PorAutorizar',
  receptorNombre: 'Cliente ficticio',
  receptorRfc: 'AAA010101AAA',
  motivo: 'Bonificacion',
  total: 1500,
  moneda: 'MXN',
  folioAw: 'NC-100',
  vendedorAw: 'Vendedor Prueba',
  facturaFolio: 'F-000001',
  fechaRecibida: '2026-10-31T10:00:00Z',
};
function sesion(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 'u@test.local', nombre: 'Prueba' },
    currentEmpresaId: 'e',
    empresas: [],
    permisos,
    errorMessage: null,
  });
}
beforeEach(() => {
  sesion(['facturacion.notas-credito.leer', 'facturacion.notas-credito.autorizar']);
  mswServer.use(
    http.get('*/api/v1/facturacion/notas-credito', () => HttpResponse.json({ items: [nc] })),
  );
});
afterEach(() =>
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    permisos: [],
    currentEmpresaId: null,
  }),
);

describe('Notas de crédito por autorizar', () => {
  it('lista la nota y autoriza solo tras confirmar, mostrando el UUID', async () => {
    let autorizadas = 0;
    mswServer.use(
      http.post('*/api/v1/facturacion/notas-credito/:id/autorizar', () => {
        autorizadas++;
        return HttpResponse.json({
          id: nc.id,
          estado: 'Timbrado',
          uuid: 'UUID-NC-1',
          folio: 'NC-000001',
          total: 1500,
        });
      }),
    );
    render(<NotasCreditoPorAutorizar />, { wrapper: createQueryWrapper() });
    await screen.findByText('NC-100');
    expect(screen.getByText('F-000001')).toBeInTheDocument();
    expect(screen.getByText('Vendedor Prueba')).toBeInTheDocument();
    expect(screen.getByText('01 · Bonificación')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    expect(autorizadas).toBe(0);
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar y timbrar' }));
    await screen.findByText(/UUID UUID-NC-1/);
    expect(autorizadas).toBe(1);
  });
  it('un rechazo del servidor se muestra con su motivo', async () => {
    mswServer.use(
      http.post('*/api/v1/facturacion/notas-credito/:id/autorizar', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'NC_IMPORTE_MAYOR_SALDO',
            detail:
              'El importe de la nota (1,500.00) es mayor que el saldo de la factura (500.00).',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<NotasCreditoPorAutorizar />, { wrapper: createQueryWrapper() });
    await screen.findByText('NC-100');
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar y timbrar' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/mayor que el saldo/));
  });
  it('sin el permiso de autorizar no hay botón', async () => {
    sesion(['facturacion.notas-credito.leer']);
    render(<NotasCreditoPorAutorizar />, { wrapper: createQueryWrapper() });
    await screen.findByText('NC-100');
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });

  it('M-087: arriba del umbral, el gerente firma y la nota espera la firma de Crédito y Cobranza', async () => {
    const grande = { ...nc, total: 46400, requiereFirmaCredito: true, firmadaGerente: false, firmadaCredito: false };
    mswServer.use(
      http.get('*/api/v1/facturacion/notas-credito', () => HttpResponse.json({ items: [grande] })),
      http.post('*/api/v1/facturacion/notas-credito/:id/autorizar', () =>
        HttpResponse.json({
          id: grande.id, estado: 'PorAutorizar', uuid: null, folio: grande.folio, total: grande.total,
          requiereFirmaCredito: true, firmadaGerente: true, firmadaCredito: false, pendiente: 'CreditoYCobranza',
        }),
      ),
    );
    render(<NotasCreditoPorAutorizar />, { wrapper: createQueryWrapper() });
    await screen.findByText('NC-100');
    expect(screen.getByText('Crédito y Cobranza: pendiente')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Firmar por Crédito y Cobranza' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Firmar' }));
    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent(/Falta la firma de Crédito y Cobranza/),
    );
  });
});
