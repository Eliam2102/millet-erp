import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { BandejaPagosACuenta } from './BandejaPagosACuenta';

let permisos: string[] = [];
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: (p: string) => permisos.includes(p) }));

const fila = (o: object = {}) => ({
  movimientoId: 'mov-1', cuentaBancariaId: 'c1', proveedorId: 'p1', proveedorClave: 'P-0412',
  proveedorRazonSocial: 'Fletes del Bajío SA de CV', monto: 18450, importeLigado: 0, moneda: 'MXN',
  fechaValor: '2026-11-03', referenciaBancaria: 'SPEI-88421', motivo: 'Flete urgente sin factura',
  estadoAplicacion: 1, antiguedadDias: 2, creadoPor: 'capturista-1', autorizadoEn: null, ...o,
});

function bandeja(items: object[]) {
  mswServer.use(
    http.get('*/api/v1/tesoreria/pagos-cuenta', () =>
      HttpResponse.json({ items, offset: 0, limit: 200, total: items.length })),
  );
  render(<BandejaPagosACuenta />, { wrapper: createQueryWrapper() });
}

describe('<BandejaPagosACuenta> firma', () => {
  beforeEach(() => {
    permisos = ['tesoreria.pagos-cuenta.autorizar'];
    useAuthStore.setState({ user: { id: 'jefe-1' } as never });
  });

  it('ofrece Autorizar a quien tiene la facultad y no capturó el pago', async () => {
    bandeja([fila()]);
    expect(await screen.findByText('Pendiente de autorización')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autorizar' })).toBeEnabled();
  });

  it('quien capturó el pago no puede autorizarlo', async () => {
    useAuthStore.setState({ user: { id: 'capturista-1' } as never });
    bandeja([fila()]);
    expect(await screen.findByRole('button', { name: 'Autorizar' })).toBeDisabled();
  });

  it('sin la facultad no ofrece Autorizar', async () => {
    permisos = [];
    bandeja([fila()]);
    await screen.findByText('Pendiente de autorización');
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });

  it('autorizar manda la Idempotency-Key', async () => {
    let visto: Request | null = null;
    mswServer.use(
      http.post('*/api/v1/tesoreria/pagos-cuenta/mov-1/autorizar', ({ request }) => {
        visto = request;
        return HttpResponse.json({});
      }),
    );
    bandeja([fila()]);
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar' }));
    await waitFor(() => expect(visto).not.toBeNull());
    expect(visto!.headers.get('Idempotency-Key')).toBeTruthy();
  });
});
