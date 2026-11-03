import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ComportamientoSerie } from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ReglasSeriePage } from '@/features/cxc/pages/ReglasSeriePage';

const BASE = '*/api/v1/cuentas-por-cobrar/liberaciones/reglas-serie/admin';
const ESPERA = { timeout: 5000 };

const regla = (prefijo: string, comportamiento: ComportamientoSerie, activo = true) => ({
  id: `r-${prefijo}`,
  prefijo,
  comportamiento,
  activo,
  version: 3,
  actualizadoEn: '2026-10-30T18:00:00Z',
  actualizadoPor: 'Crédito y Cobranza',
});

const REGLAS = [
  regla('5500', ComportamientoSerie.SiempreLibera),
  regla('7800', ComportamientoSerie.NuncaLibera),
  regla('6100', ComportamientoSerie.EvaluaCredito, false),
];

const HISTORIAL = [
  {
    id: 'h-1',
    reglaId: 'r-5500',
    prefijo: '5500',
    operacion: 'actualizar',
    comportamientoAntes: ComportamientoSerie.SiempreLibera,
    comportamientoDespues: ComportamientoSerie.EvaluaCredito,
    activoAntes: null,
    activoDespues: null,
    actorNombre: 'Gerencia de Crédito',
    fecha: '2026-10-30T19:00:00Z',
  },
  {
    id: 'h-2',
    reglaId: 'r-7800',
    prefijo: '7800',
    operacion: 'crear',
    comportamientoAntes: null,
    comportamientoDespues: ComportamientoSerie.NuncaLibera,
    activoAntes: null,
    activoDespues: true,
    actorNombre: 'Sistema',
    fecha: '2026-10-30T17:00:00Z',
  },
];

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['cuentas_por_cobrar.reglas-serie.gestionar'],
    errorMessage: null,
  });
  mswServer.use(
    http.get(BASE, () => HttpResponse.json(REGLAS)),
    http.get(`${BASE}/historial`, () => HttpResponse.json(HISTORIAL)),
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

describe('<ReglasSeriePage> — C7.2', () => {
  it('lista las reglas con su comportamiento, estado y último cambio', async () => {
    render(<ReglasSeriePage />, { wrapper: createQueryWrapper() });

    const fila5500 = (await screen.findAllByText('5500', undefined, ESPERA))[0].closest('tr')!;
    expect(within(fila5500).getByText('Siempre libera')).toBeInTheDocument();
    expect(within(fila5500).getByText('Activa')).toBeInTheDocument();
    expect(within(fila5500).getByText('Crédito y Cobranza')).toBeInTheDocument();

    const fila6100 = screen.getByText('6100').closest('tr')!;
    expect(within(fila6100).getByText('Inactiva')).toBeInTheDocument();
    expect(within(fila6100).getByRole('button', { name: 'Activar' })).toBeInTheDocument();
  });

  it('muestra el historial de cambios con quién y qué cambió', async () => {
    render(<ReglasSeriePage />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByText('Siempre libera → Evalúa crédito', undefined, ESPERA),
    ).toBeInTheDocument();
    expect(screen.getByText('Gerencia de Crédito')).toBeInTheDocument();
    expect(screen.getByText('Alta: nunca libera (solo con autorización)')).toBeInTheDocument();
  });

  it('desactivar manda la versión esperada y el nuevo estado', async () => {
    let recibido: { version: string | null; body: unknown } | null = null;
    mswServer.use(
      http.put(`${BASE}/r-7800/estado`, async ({ request }) => {
        recibido = {
          version: request.headers.get('X-Expected-Version'),
          body: await request.json(),
        };
        return HttpResponse.json({ ...REGLAS[1], activo: false, version: 4 });
      }),
    );
    render(<ReglasSeriePage />, { wrapper: createQueryWrapper() });

    const fila7800 = (await screen.findAllByText('7800', undefined, ESPERA))[0].closest('tr')!;
    fireEvent.click(within(fila7800).getByRole('button', { name: 'Desactivar' }));

    await waitFor(() => expect(recibido).not.toBeNull(), ESPERA);
    expect(recibido).toEqual({ version: '3', body: { activo: false } });
  });

  it('alta: valida que la serie sean dígitos y envía la regla', async () => {
    let body: unknown = null;
    mswServer.use(
      http.post(BASE, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(regla('6200', ComportamientoSerie.EvaluaCredito), { status: 201 });
      }),
    );
    render(<ReglasSeriePage />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: /Agregar serie/i }, ESPERA));
    const input = screen.getByLabelText('Serie');
    const guardar = screen.getByRole('button', { name: 'Guardar' });

    fireEvent.change(input, { target: { value: '62A0' } });
    expect(screen.getByRole('alert')).toHaveTextContent(/solo dígitos/);
    expect(guardar).toBeDisabled();

    fireEvent.change(input, { target: { value: '6200' } });
    fireEvent.click(guardar);

    await waitFor(() => expect(body).not.toBeNull(), ESPERA);
    expect(body).toEqual({ prefijo: '6200', comportamiento: ComportamientoSerie.EvaluaCredito });
  });

  it('error al cargar las reglas', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<ReglasSeriePage />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByText(/No se pudieron cargar las reglas de serie/i, undefined, ESPERA),
    ).toBeInTheDocument();
  });
});
