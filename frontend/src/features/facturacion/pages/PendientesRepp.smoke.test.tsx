import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PendientesRepp } from './PendientesRepp';
import type { ReppPendiente } from '../api/useReppPendientes';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
}));
vi.mock('../components/FacturaPpdPicker', () => ({
  FacturaPpdPicker: () => <button>Agregar factura</button>,
}));
vi.mock('@/components/erp/selectors/FormaPagoSelector', () => ({
  FormaPagoSelector: () => <button aria-label="Forma de pago">03</button>,
}));
const pago: ReppPendiente = {
  id: '00000000-0000-0000-0000-000000000001',
  clienteId: 'c',
  clienteNombre: 'Cliente ficticio',
  clienteRfc: 'AAA010101AAA',
  movimientoBancarioId: 'm',
  cuentaBancariaId: 'b',
  propuestaId: null,
  monto: 100,
  moneda: 'MXN',
  fechaValor: '2026-10-28',
  fechaLimite: '2026-11-05',
  referencia: 'DEP-DEMO',
  formaPago: '03',
  revisado: false,
  estado: 'Pendiente',
  alerta: 'Cerca del plazo',
  tipoCambio: null,
  tcPorRegistrar: false,
  reciboPagoId: null,
  intentoReciboPagoId: null,
  ultimoErrorCodigo: null,
  ultimoErrorMensaje: null,
  motivoDescarte: null,
  facturas: [],
  bloqueos: ['Relaciona al menos una factura PPD.'],
};
beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 'u@test.local', nombre: 'Prueba' },
    currentEmpresaId: 'e',
    empresas: [],
    permisos: ['facturacion.facturas.leer', 'facturacion.repp.emitir'],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/facturacion/repp/pendientes', () =>
      HttpResponse.json({
        items: [pago],
        total: 1,
        kpis: { pendientes: 1, cercaDelPlazo: 1, vencidos: 0, conError: 0 },
      }),
    ),
    http.get('*/api/v1/facturacion/repp/pendientes/:id', () => HttpResponse.json(pago)),
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
describe('Pendientes de REP', () => {
  it('muestra KPIs, tabs y revisión sin timbrar al abrir', async () => {
    const enviar = vi.fn();
    mswServer.use(
      http.post('*/api/v1/facturacion/repp/pendientes/:id/emitir', () => {
        enviar();
        return HttpResponse.json({});
      }),
    );
    render(<PendientesRepp />, { wrapper: createQueryWrapper() });
    await screen.findByText('Cliente ficticio');
    expect(screen.getByRole('tab', { name: 'Descartados' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Emitir seleccionados/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Revisar' }));
    await screen.findByText('Propuesta: 03 · confirmar');
    expect(screen.getByRole('button', { name: 'Emitir REP' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Emitir REP' })).toHaveAttribute('title');
    expect(enviar).not.toHaveBeenCalled();
  });
  it('solo lectura no ofrece selección ni escritura', async () => {
    useAuthStore.setState({ permisos: ['facturacion.facturas.leer'] });
    render(<PendientesRepp />, { wrapper: createQueryWrapper() });
    await screen.findByText('Cliente ficticio');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Emitir seleccionados/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ver' }));
    await screen.findByText('Revisar pago confirmado');
    expect(screen.queryByRole('button', { name: 'Guardar revisión' })).not.toBeInTheDocument();
  });
  it('el KPI filtra en el servidor', async () => {
    const filtros: string[] = [];
    mswServer.use(
      http.get('*/api/v1/facturacion/repp/pendientes', ({ request }) => {
        filtros.push(new URL(request.url).searchParams.get('indicador') ?? '');
        return HttpResponse.json({
          items: [],
          total: 0,
          kpis: { pendientes: 0, cercaDelPlazo: 0, vencidos: 0, conError: 0 },
        });
      }),
    );
    render(<PendientesRepp />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /Vencidos/ }));
    await waitFor(() => expect(filtros).toContain('vencidos'));
  });
});
