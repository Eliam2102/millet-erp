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
  it('86e3tfdve: muestra la beneficiaria prellenada y envía la ordenante al emitir', async () => {
    const cuerpos: unknown[] = [];
    const listo: ReppPendiente = {
      ...pago,
      revisado: true,
      facturas: [{ facturaVentaId: '00000000-0000-0000-0000-0000000000f1', folio: 'F-1', importe: 100 }],
      bloqueos: [],
      cuentaBancariaEtiqueta: 'Banco ficticio · •••• 0010',
      cuentaBeneficiariaSugerida: '•••• 0018',
    };
    mswServer.use(
      http.get('*/api/v1/facturacion/repp/pendientes/:id', () => HttpResponse.json(listo)),
      http.post('*/api/v1/facturacion/repp/pendientes/:id/emitir', async ({ request }) => {
        cuerpos.push(await request.json());
        return HttpResponse.json({ id: listo.id, emitido: true, reciboPagoId: 'r', codigo: null, mensaje: null });
      }),
    );
    render(<PendientesRepp />, { wrapper: createQueryWrapper() });
    await screen.findByText('Cliente ficticio');
    fireEvent.click(screen.getByRole('button', { name: 'Revisar' }));
    expect(await screen.findByText(/•••• 0018/)).toBeInTheDocument();
    expect(screen.getByText(/Banco ficticio · •••• 0010/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Cuenta ordenante'), { target: { value: '000000000012' } });
    expect(await screen.findByText(/se esperan 10, 16 o 18 dígitos/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Emitir REP' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Cuenta ordenante'), { target: { value: '0000000000000016' } });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Emitir REP' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Emitir REP' }));
    await waitFor(() => expect(cuerpos).toHaveLength(1));
    expect(cuerpos[0]).toEqual({ cuentaOrdenante: '0000000000000016', cuentaBeneficiaria: null });
  });

  it('86e3tfdve opción A: muestra la ordenante del depósito enmascarada y permite quitarla al guardar', async () => {
    const cuerpos: unknown[] = [];
    const conOrdenante: ReppPendiente = {
      ...pago,
      revisado: true,
      facturas: [{ facturaVentaId: '00000000-0000-0000-0000-0000000000f1', folio: 'F-1', importe: 100 }],
      bloqueos: [],
      cuentaOrdenante: '•••• 0016',
    };
    mswServer.use(
      http.get('*/api/v1/facturacion/repp/pendientes/:id', () => HttpResponse.json(conOrdenante)),
      http.put('*/api/v1/facturacion/repp/pendientes/:id', async ({ request }) => {
        cuerpos.push(await request.json());
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<PendientesRepp />, { wrapper: createQueryWrapper() });
    await screen.findByText('Cliente ficticio');
    fireEvent.click(screen.getByRole('button', { name: 'Revisar' }));
    expect(await screen.findByText('•••• 0016')).toBeInTheDocument();
    expect(screen.getByLabelText('Corregir cuenta ordenante')).toHaveValue('');
    expect(screen.getByText(/usar la que registró Tesorería/)).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Quitar la cuenta ordenante del pago'));
    expect(screen.getByText('Se quitará al guardar')).toBeInTheDocument();
    expect(screen.getByLabelText('Corregir cuenta ordenante')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Emitir REP' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Guardar revisión' }));
    await waitFor(() => expect(cuerpos).toHaveLength(1));
    expect(cuerpos[0]).toMatchObject({ cuentaOrdenante: null, limpiarCuentaOrdenante: true });
  });

  it('86e3tfdve opción A: el resultado del lote muestra el aviso de REP sin ordenante', async () => {
    mswServer.use(
      http.post('*/api/v1/facturacion/repp/pendientes/emitir-lote', () =>
        HttpResponse.json([
          {
            id: pago.id,
            emitido: true,
            reciboPagoId: 'r',
            codigo: null,
            mensaje: null,
            aviso: 'Se emitió sin cuenta ordenante: •••• 0011 no aplica a la forma de pago 03.',
          },
        ]),
      ),
    );
    render(<PendientesRepp />, { wrapper: createQueryWrapper() });
    await screen.findByText('Cliente ficticio');
    fireEvent.click(screen.getByRole('checkbox', { name: /Seleccionar/ }));
    fireEvent.click(screen.getByRole('button', { name: /Emitir seleccionados/ }));
    expect(await screen.findByText(/Se emitió sin cuenta ordenante: •••• 0011/)).toBeInTheDocument();
    expect(screen.getByText('REP emitido')).toBeInTheDocument();
  });
});
