import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ConciliacionCarteraPage } from '@/features/cxc/pages/ConciliacionCarteraPage';

const BASE = '*/api/v1/cuentas-por-cobrar/conciliaciones';
const ESPERA = { timeout: 5000 };

const corrida = (id: string, fecha: string, totalDiferencias: number, origen = 'Programada') => ({
  id,
  fecha,
  origen,
  ejecutadaPor: null,
  ejecutadaEn: `${fecha}T12:00:00Z`,
  comprobantesRevisados: 14,
  totalDiferencias,
});

const CORRIDAS = [corrida('c-2', '2026-11-02', 1), corrida('c-1', '2026-11-01', 0)];

const DETALLE_C2 = {
  corrida: CORRIDAS[0],
  reporte: {
    titulo: 'Conciliación de cartera del 02/11/2026',
    generadoEn: '2026-11-03T12:00:00Z',
    filtrosAplicados: [{ label: 'Día conciliado', valor: '02/11/2026' }],
    columnas: [
      { key: 'tipo', label: 'Diferencia', tipo: 8, alineacion: 1 },
      { key: 'cliente', label: 'Cliente', tipo: 1, alineacion: 1 },
      { key: 'folio', label: 'Folio', tipo: 1, alineacion: 1 },
      { key: 'importeFacturacion', label: 'Importe en Facturación', tipo: 4, alineacion: 3 },
      { key: 'causa', label: 'Causa probable', tipo: 1, alineacion: 1 },
    ],
    filas: [
      {
        tipo: 'Factura no está en cartera',
        cliente: '[CLIENTE]',
        folio: 'FV-100',
        importeFacturacion: 1160,
        causa: 'La factura está timbrada y no está en cartera: no se procesó el evento de factura timbrada.',
      },
    ],
    totales: null,
  },
};

function sesion(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  sesion(['cuentas_por_cobrar.cartera.leer', 'cuentas_por_cobrar.conciliacion.ejecutar']);
  mswServer.use(
    http.get(BASE, () => HttpResponse.json(CORRIDAS)),
    http.get(`${BASE}/c-2`, () => HttpResponse.json(DETALLE_C2)),
  );
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

describe('<ConciliacionCarteraPage> — C7.7', () => {
  it('lista las corridas con su resultado y abre las diferencias de la más reciente', async () => {
    render(<ConciliacionCarteraPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Con diferencia', undefined, ESPERA)).toBeInTheDocument();
    expect(screen.getByText('Sin diferencias')).toBeInTheDocument();
    expect(await screen.findByText('Factura no está en cartera', undefined, ESPERA)).toBeInTheDocument();
    expect(screen.getByText('FV-100')).toBeInTheDocument();
  });

  it('ordena las corridas por fecha y hora de ejecución, la más reciente arriba', async () => {
    const reciente = { ...corrida('c-9', '2026-10-15', 0, 'Demanda'), ejecutadaEn: '2026-11-03T18:30:00Z' };
    mswServer.use(
      http.get(BASE, () => HttpResponse.json([...CORRIDAS, reciente])),
      http.get(`${BASE}/c-9`, () => HttpResponse.json({ ...DETALLE_C2, corrida: reciente })),
    );
    render(<ConciliacionCarteraPage />, { wrapper: createQueryWrapper() });

    await screen.findByText('Con diferencia', undefined, ESPERA);
    const filas = screen.getAllByRole('row').slice(1);
    expect(filas).toHaveLength(3);
    expect(within(filas[0]!).getByText('A demanda')).toBeInTheDocument();
    expect(within(filas[0]!).getByRole('button')).toHaveAttribute('aria-current', 'true');
    expect(within(filas[1]!).getByText('Con diferencia')).toBeInTheDocument();
    expect(within(filas[2]!).getByText('Sin diferencias')).toBeInTheDocument();
  });

  it('conciliar a demanda manda el día elegido con llave de idempotencia', async () => {
    let recibido: { llave: string | null; body: unknown } | null = null;
    mswServer.use(
      http.post(BASE, async ({ request }) => {
        recibido = { llave: request.headers.get('Idempotency-Key'), body: await request.json() };
        return HttpResponse.json(corrida('c-3', '2026-10-31', 0, 'Demanda'), { status: 201 });
      }),
      http.get(`${BASE}/c-3`, () =>
        HttpResponse.json({ ...DETALLE_C2, corrida: corrida('c-3', '2026-10-31', 0, 'Demanda') }),
      ),
    );
    render(<ConciliacionCarteraPage />, { wrapper: createQueryWrapper() });

    const fecha = await screen.findByLabelText('Día a conciliar', undefined, ESPERA);
    fireEvent.change(fecha, { target: { value: '2026-10-31' } });
    fireEvent.click(screen.getByRole('button', { name: 'Conciliar día' }));

    await waitFor(() => expect(recibido).not.toBeNull(), ESPERA);
    expect(recibido!.body).toEqual({ fecha: '2026-10-31' });
    expect(recibido!.llave).toBeTruthy();
  });

  it('sin permiso de conciliar el botón explica por qué está deshabilitado', async () => {
    sesion(['cuentas_por_cobrar.cartera.leer']);
    render(<ConciliacionCarteraPage />, { wrapper: createQueryWrapper() });

    const boton = await screen.findByRole('button', { name: 'Conciliar día' }, ESPERA);
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute('title', 'Necesitas el permiso para conciliar la cartera.');
  });

  it('sin corridas invita a conciliar un día', async () => {
    mswServer.use(http.get(BASE, () => HttpResponse.json([])));
    render(<ConciliacionCarteraPage />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByText('Todavía no hay conciliaciones', undefined, ESPERA),
    ).toBeInTheDocument();
  });
});
