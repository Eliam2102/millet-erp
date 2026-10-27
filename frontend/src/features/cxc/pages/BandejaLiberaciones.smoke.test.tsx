import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaLiberaciones } from '@/features/cxc/pages/BandejaLiberaciones';
import { ReglaAplicadaLiberacion, ResultadoLiberacion } from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const LIB = '*/api/v1/cuentas-por-cobrar/liberaciones';
const AUT = '*/api/v1/cuentas-por-cobrar/autorizaciones';
const LOOKUP = '*/api/v1/cuentas-por-cobrar/clientes-lookup';
const USUARIOS = '*/api/v1/identidad/usuarios';

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_cobrar.liberacion.decidir',
      'cuentas_por_cobrar.liberacion.override',
      'cuentas_por_cobrar.lineas-credito.leer',
    ],
    errorMessage: null,
  });
  // Defaults compartidos: bandejas vacías salvo override por test.
  mswServer.use(
    http.get(AUT, () => HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 })),
    http.get(LOOKUP, () => HttpResponse.json([])),
    http.get(USUARIOS, () => HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 })),
    http.get(`${AUT}/beneficiarios`, () => HttpResponse.json([])),
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

describe('<BandejaLiberaciones> — smoke', () => {
  it('estado empty + acciones visibles con permisos', async () => {
    mswServer.use(
      http.get(LIB, () => HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 })),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin decisiones de liberación/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /Decidir liberación/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Nueva autorización/i })).toBeInTheDocument();
  });

  it('renderiza una decisión con resultado, regla y cliente resuelto', async () => {
    mswServer.use(
      http.get(LIB, () =>
        HttpResponse.json({
          items: [
            {
              id: 'dl-1',
              pedidoRef: '3000123456',
              clienteId: 'cli-1',
              moneda: 'MXN',
              montoPedido: 15000,
              creditoDisponibleSnapshot: 38000,
              resultado: ResultadoLiberacion.LiberadoConOverride,
              reglaAplicada: ReglaAplicadaLiberacion.Override,
              overrideId: 'ac-1',
              decididoPor: 'u-test',
              decididoEn: '2026-07-14T10:00:00Z',
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
      http.get(LOOKUP, () =>
        HttpResponse.json([
          { id: 'cli-1', clave: 'C001', rfc: 'AAA010101AAA', razonSocial: 'ACME SA' },
        ]),
      ),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('3000123456')).toBeInTheDocument());
    // El lookup de nombres resuelve DESPUÉS de la lista (query encadenada
    // por ids) — findByText espera ese segundo ciclo.
    expect(await screen.findByText('ACME SA')).toBeInTheDocument();
    expect(
      within(screen.getByRole('table')).getByText('Liberado con override'),
    ).toBeInTheDocument();
    expect(screen.getByText('Override autorizado')).toBeInTheDocument();
  });

  it('sin permiso override: no muestra "Nueva autorización"', async () => {
    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.liberacion.decidir'],
    });
    mswServer.use(
      http.get(LIB, () => HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 })),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin decisiones de liberación/i)).toBeInTheDocument(),
    );
    expect(screen.queryByRole('button', { name: /Nueva autorización/i })).not.toBeInTheDocument();
  });

  it('estado error con retry', async () => {
    mswServer.use(
      http.get(LIB, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/No se pudieron cargar las decisiones/i)).toBeInTheDocument(),
    );
  });

  // ------------------------------------------------ C7.3

  const pendiente = (overrides: Record<string, unknown> = {}) => ({
    id: 'dl-p',
    pedidoRef: '9000102',
    clienteId: 'cli-1',
    moneda: 'MXN',
    montoPedido: 60000,
    creditoDisponibleSnapshot: 40000,
    resultado: ResultadoLiberacion.PendienteAutorizacion,
    reglaAplicada: ReglaAplicadaLiberacion.Cobertura,
    overrideId: null,
    decididoPor: 'u-coord',
    decididoEn: '2026-10-25T09:00:00Z',
    porcentajeCobertura: 66.66,
    decisionOrigenId: null,
    resolucionId: null,
    ...overrides,
  });

  const listar = (items: unknown[]) =>
    http.get(LIB, () => HttpResponse.json({ items, offset: 0, limit: 200, total: items.length }));

  it('C7.3: pendiente muestra estado, cobertura y «Autorizar»; motivo obligatorio y POST al autorizar', async () => {
    let body: unknown = null;
    mswServer.use(
      listar([pendiente()]),
      http.post(`${LIB}/dl-p/autorizar`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(
          pendiente({
            id: 'dl-r',
            resultado: ResultadoLiberacion.LiberadoConOverride,
            reglaAplicada: ReglaAplicadaLiberacion.Override,
            overrideId: 'ac-9',
            decisionOrigenId: 'dl-p',
            decididoPor: 'u-test',
          }),
          { status: 201 },
        );
      }),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('9000102')).toBeInTheDocument());
    const tabla = screen.getByRole('table');
    expect(within(tabla).getByText('Pendiente de autorización')).toBeInTheDocument();
    expect(within(tabla).getByText('Cobertura 66 %')).toBeInTheDocument();
    expect(within(tabla).getByText('Cobertura parcial')).toBeInTheDocument();
    // La pestaña de pendientes existe en el filtro de resultado.
    expect(screen.getAllByText('Pendiente de autorización').length).toBeGreaterThan(1);

    fireEvent.click(within(tabla).getByRole('button', { name: /Autorizar/i }));
    const dialogo = await screen.findByRole('dialog');
    expect(within(dialogo).getByText('Cobertura 66 %')).toBeInTheDocument();

    fireEvent.click(within(dialogo).getByRole('button', { name: /Autorizar y liberar/i }));
    expect(await within(dialogo).findByText('El motivo es obligatorio')).toBeInTheDocument();
    expect(body).toBeNull();

    fireEvent.change(within(dialogo).getByLabelText(/Motivo/i), {
      target: { value: 'Pago en tránsito confirmado' },
    });
    fireEvent.click(within(dialogo).getByRole('button', { name: /Autorizar y liberar/i }));
    await waitFor(() => expect(body).toEqual({ motivo: 'Pago en tránsito confirmado' }));
  });

  it('C7.3: sin autoconsumo — quien registró la pendiente no ve «Autorizar» ni «Rechazar»', async () => {
    mswServer.use(listar([pendiente({ decididoPor: 'u-test' })]));
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('9000102')).toBeInTheDocument());
    const tabla = screen.getByRole('table');
    expect(within(tabla).queryByRole('button', { name: /Autorizar/i })).not.toBeInTheDocument();
    expect(within(tabla).queryByRole('button', { name: /Rechazar/i })).not.toBeInTheDocument();
  });

  it('C7.3: pendiente ya resuelta muestra «Ya resuelta» sin acciones', async () => {
    mswServer.use(listar([pendiente({ resolucionId: 'dl-r' })]));
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Ya resuelta')).toBeInTheDocument());
    const tabla = screen.getByRole('table');
    expect(within(tabla).queryByRole('button', { name: /Autorizar/i })).not.toBeInTheDocument();
    expect(within(tabla).queryByRole('button', { name: /Rechazar/i })).not.toBeInTheDocument();
  });

  it('C7.3: muestra el nombre de quien evaluó y el crédito al evaluar', async () => {
    mswServer.use(
      listar([pendiente()]),
      http.get(`${AUT}/beneficiarios`, () =>
        HttpResponse.json([{ id: 'u-coord', nombre: 'Coordinador Demo', email: 'c@e.com' }]),
      ),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    const tabla = await screen.findByRole('table');
    expect(await within(tabla).findByText('Coordinador Demo')).toBeInTheDocument();
    expect(within(tabla).getByText('$40,000.00 MXN')).toBeInTheDocument();
  });

  it('C7.3: «Rechazar» exige motivo y hace POST a /rechazar', async () => {
    let body: unknown = null;
    mswServer.use(
      listar([pendiente()]),
      http.post(`${LIB}/dl-p/rechazar`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(
          pendiente({
            id: 'dl-x',
            resultado: ResultadoLiberacion.Retenido,
            decisionOrigenId: 'dl-p',
            decididoPor: 'u-test',
            motivo: 'Pagos atrasados',
          }),
          { status: 201 },
        );
      }),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('9000102')).toBeInTheDocument());

    fireEvent.click(within(screen.getByRole('table')).getByRole('button', { name: /Rechazar/i }));
    const dialogo = await screen.findByRole('dialog');
    expect(within(dialogo).getByText('Rechazar liberación')).toBeInTheDocument();

    fireEvent.click(within(dialogo).getByRole('button', { name: /Rechazar y retener/i }));
    expect(await within(dialogo).findByText('El motivo es obligatorio')).toBeInTheDocument();
    expect(body).toBeNull();

    fireEvent.change(within(dialogo).getByLabelText(/Motivo/i), {
      target: { value: 'Pagos atrasados' },
    });
    fireEvent.click(within(dialogo).getByRole('button', { name: /Rechazar y retener/i }));
    await waitFor(() => expect(body).toEqual({ motivo: 'Pagos atrasados' }));
  });

  it('C7.3: sin permiso override no se ofrece «Autorizar»', async () => {
    useAuthStore.setState({ permisos: ['cuentas_por_cobrar.liberacion.decidir'] });
    mswServer.use(listar([pendiente()]));
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('9000102')).toBeInTheDocument());
    expect(
      within(screen.getByRole('table')).queryByRole('button', { name: /Autorizar/i }),
    ).not.toBeInTheDocument();
  });
});
