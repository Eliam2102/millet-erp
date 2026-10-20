import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PorCobrarCaja } from '@/features/facturacion/components/PorCobrarCaja';
import { useAuthStore } from '@/lib/auth/auth-store';

const cobrables = [
  {
    comprobanteId: 'c-f1',
    folio: 'FIX12-UZ-F1',
    tipo: 'Ingreso',
    receptorNombre: 'CLIENTE FIX12 PRUEBA',
    total: 1044,
    moneda: 'MXN',
    fechaTimbrado: '2026-10-13T20:00:00Z',
    montoAcreditado: 116,
    montoRanura: 0,
  },
  {
    comprobanteId: 'c-p1',
    folio: 'FIX12-UZ-P1',
    tipo: 'Pago',
    receptorNombre: 'CLIENTE FIX12 PRUEBA',
    total: 750,
    moneda: 'XXX',
    fechaTimbrado: '2026-10-13T20:00:00Z',
    montoAcreditado: 0,
    montoRanura: 0,
  },
];

const sesion = {
  id: 'ses-1',
  cajaId: 'c-1',
  cajaNombre: 'CAJA PRUEBA 1',
  sucursalId: 's-1',
  responsableUsuarioId: 'u-test',
  estado: 'Abierta',
  diaOperacion: '2026-10-13',
  fechaApertura: '2026-10-13T14:00:00Z',
  fechaCierre: null,
  fondoApertura: 1000,
  efectivoTeorico: null,
  efectivoDeclarado: null,
  diferencia: null,
  cierreExtemporaneo: false,
  notasCierre: null,
  autorizacionAperturaId: null,
  version: 1,
  cortes: [],
  movimientos: [],
  totalesPorForma: [],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.caja.operar'],
    errorMessage: null,
  });
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

describe('PorCobrarCaja (FIX-12)', () => {
  it('lista lo que se puede cobrar y «Cobrar» abre el formulario en el panel lateral', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/cobros/cobrables', () => HttpResponse.json(cobrables)),
      http.get('*/api/v1/facturacion/cajas/sesion-actual', () =>
        HttpResponse.json({ sesion, diaAnteriorPendiente: false }),
      ),
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([{ id: 'ef', claveSat: '01', descripcion: 'Efectivo', activa: true }]),
      ),
    );
    render(<PorCobrarCaja />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('FIX12-UZ-F1')).toBeInTheDocument());
    expect(screen.getByText('Complemento de pago')).toBeInTheDocument();
    expect(screen.getByText(/Neto de NC/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cobrar FIX12-UZ-F1' }));

    const panel = await screen.findByRole('dialog');
    expect(within(panel).getByText('Cobrar FIX12-UZ-F1')).toBeInTheDocument();
    // El formulario arranca abierto: sin el botón intermedio «Registrar cobro».
    await waitFor(() =>
      expect(within(panel).getByRole('button', { name: 'Cobrar' })).toBeInTheDocument(),
    );
    expect(within(panel).queryByRole('button', { name: 'Registrar cobro' })).toBeNull();
  });

  it('manda la búsqueda al endpoint y avisa cuando no hay coincidencias', async () => {
    const buscados: (string | null)[] = [];
    mswServer.use(
      http.get('*/api/v1/facturacion/cobros/cobrables', ({ request }) => {
        const search = new URL(request.url).searchParams.get('search');
        buscados.push(search);
        return HttpResponse.json(search == null ? cobrables : []);
      }),
    );
    render(<PorCobrarCaja />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FIX12-UZ-F1')).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText('Buscar comprobantes por cobrar'), {
      target: { value: 'NO-EXISTE' },
    });

    await waitFor(() =>
      expect(screen.getByText('Ningún comprobante por cobrar coincide con «NO-EXISTE».')).toBeInTheDocument(),
    );
    expect(buscados).toContain('NO-EXISTE');
  });

  it('muestra un fallo de consulta sin afirmar que no hay comprobantes y permite reintentar', async () => {
    let consultas = 0;
    mswServer.use(
      http.get('*/api/v1/facturacion/cobros/cobrables', () => {
        consultas += 1;
        return consultas === 1
          ? HttpResponse.json({ title: 'Consulta no disponible', status: 500 }, { status: 500 })
          : HttpResponse.json(cobrables);
      }),
    );
    render(<PorCobrarCaja />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('No se pudieron cargar los comprobantes por cobrar')).toBeInTheDocument();
    expect(screen.queryByText('No hay comprobantes por cobrar en tu caja.')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByText('FIX12-UZ-F1')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(consultas).toBe(2);
  });

  it('muestra MXN al cobrar un complemento cuyo CFDI conserva la moneda XXX', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/cobros/cobrables', () => HttpResponse.json(cobrables)),
      http.get('*/api/v1/facturacion/cajas/sesion-actual', () =>
        HttpResponse.json({ sesion, diaAnteriorPendiente: false }),
      ),
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([{ id: 'ef', claveSat: '01', descripcion: 'Efectivo', activa: true }]),
      ),
    );
    render(<PorCobrarCaja />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Cobrar FIX12-UZ-P1' }));

    const panel = await screen.findByRole('dialog');
    expect(await within(panel).findByText('Cobrar FIX12-UZ-P1 — total 750.00 MXN')).toBeInTheDocument();
    expect(within(panel).queryByText(/750\.00 XXX/)).not.toBeInTheDocument();
  });
});

it('muestra diez filas y pagina el resto', async () => {
  mswServer.use(http.get('*/api/v1/facturacion/cobros/cobrables', () => HttpResponse.json(
    Array.from({ length: 11 }, (_, i) => ({ ...cobrables[0], comprobanteId: `c-${i}`, folio: `FOLIO-${i}` })),
  )));
  render(<PorCobrarCaja />, { wrapper: createQueryWrapper() });
  await screen.findByText('FOLIO-0');
  expect(screen.getByText('FOLIO-9')).toBeInTheDocument();
  expect(screen.queryByText('FOLIO-10')).not.toBeInTheDocument();
  expect(screen.getAllByRole('button', { name: /^Cobrar FOLIO/ })).toHaveLength(10);
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
  expect(screen.getByText('FOLIO-10')).toBeInTheDocument();
  expect(screen.queryByText('FOLIO-0')).not.toBeInTheDocument();
});

it('abre por identificador y folio y permite cerrar el cobro directo', async () => {
  window.history.replaceState({}, '', '/facturacion/caja?cobrar=c-f1&folio=FIX12-UZ-F1');
  mswServer.use(
    http.get('*/api/v1/facturacion/cobros/cobrables', () => HttpResponse.json(cobrables)),
    http.get('*/api/v1/facturacion/cajas/sesion-actual', () => HttpResponse.json({ sesion, diaAnteriorPendiente: false })),
  );
  try {
    render(<PorCobrarCaja />, { wrapper: createQueryWrapper() });
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Cobrar FIX12-UZ-F1')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  } finally {
    window.history.replaceState({}, '', '/');
  }
});
