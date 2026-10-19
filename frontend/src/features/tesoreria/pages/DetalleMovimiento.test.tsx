import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleMovimiento } from './DetalleMovimiento';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  Link: ({ children }: { children: React.ReactNode }) => <a href="#">{children}</a>,
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));

const base = {
  id: 'mov-1', sentido: 'Egreso', monto: 100, moneda: 'MXN', fechaValor: '2026-10-01',
  estadoAplicacion: 'Aplicado', estadoConciliacion: 'Pendiente', beneficiarioTipo: null,
  beneficiarioRef: null, contramovimientoDe: null, motivoNoAplicado: null,
  creadoPor: 'u', creadoEn: '2026-10-01T00:00:00Z', version: 1,
};

function detalle(movimiento: object) {
  mswServer.use(
    http.get('*/api/v1/tesoreria/movimientos/mov-1', () =>
      HttpResponse.json({ movimiento: { ...base, ...movimiento }, aplicaciones: [], contramovimientos: [] })),
  );
  render(<DetalleMovimiento id="mov-1" />, { wrapper: createQueryWrapper() });
}

describe('<DetalleMovimiento> fondos verificados', () => {
  it('no lo muestra si el movimiento no tiene cuenta destino', async () => {
    detalle({ fondosVerificados: true, destinoBanco: null });
    await screen.findByText('Estados');
    expect(screen.queryByText('Fondos verificados')).not.toBeInTheDocument();
  });

  it('lo muestra con cuenta destino', async () => {
    detalle({ fondosVerificados: true, destinoBanco: 'Banco', destinoNumeroCuenta: '••••1' });
    expect(await screen.findByText('Fondos verificados')).toBeInTheDocument();
  });
});
