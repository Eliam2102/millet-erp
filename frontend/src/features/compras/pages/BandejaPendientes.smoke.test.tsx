import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaPendientes } from '@/features/compras/pages/BandejaPendientes';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  DisponibilidadPendiente,
  EstadoRequisicion,
  NivelAutorizacion,
  OrigenRequisicion,
  Prioridad,
  type PendienteAutorizacionItemResponse,
} from '@/features/compras/api/types';

// Holder mutable para variar los search params por test (TanStack useSearch).
const searchHolder = vi.hoisted(() => ({
  current: { offset: 0, limit: 50 } as Record<string, unknown>,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    state,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    state?: unknown;
  }) => (
    <a href={to} className={className} data-state={JSON.stringify(state)}>
      {children}
    </a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchHolder.current,
}));

function setupCatalogos() {
  mswServer.use(
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
}

beforeEach(() => {
  searchHolder.current = { offset: 0, limit: 50 };
  setupCatalogos();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['compras.requisiciones.autorizar-nivel1'],
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

describe('<BandejaPendientes> — smoke', () => {
  it('estado empty: muestra texto específico de pendientes', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/no hay requisiciones pendientes/i),
      ).toBeInTheDocument(),
    );
  });

  it('estado con datos: renderiza la tabla con folios', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-1',
              folio: 'MID2026-000050',
              folioAnio: 2026,
              estado: EstadoRequisicion.EnAutorizacion,
              clasificacion: Clasificacion.Servicio,
              prioridad: Prioridad.Normal,
              sucursalId: 's-1',
              departamentoId: 'd-1',
              requisitanteId: 'u-1',
              descripcion: null,
              fechaSolicitud: '2026-05-09T10:00:00Z',
              fechaEntregaDeseada: null,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    // El folio aparece en la tabla (desktop) Y en la card (mobile);
    // ambas se renderizan en el DOM y CSS oculta una según breakpoint.
    await waitFor(() =>
      expect(screen.getAllByText('MID2026-000050').length).toBeGreaterThan(0),
    );
  });

  it('header "Pendientes de autorización" presente', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getAllByText(/Pendientes de autorización/i).length,
      ).toBeGreaterThan(0),
    );
  });

  it('muestra el badge de nivel pendiente por item (PR-A)', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-1',
              folio: 'MID2026-000051',
              folioAnio: 2026,
              estado: EstadoRequisicion.EnAutorizacion,
              clasificacion: Clasificacion.Servicio,
              prioridad: Prioridad.Normal,
              sucursalId: 's-1',
              departamentoId: 'd-1',
              requisitanteId: 'u-1',
              descripcion: null,
              fechaSolicitud: '2026-05-09T10:00:00Z',
              fechaEntregaDeseada: null,
              nivelPendiente: NivelAutorizacion.Nivel2,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    // Aparece en tabla (desktop) y card (mobile); ambas se renderizan.
    await waitFor(() =>
      expect(screen.getAllByText('Falta N2').length).toBeGreaterThan(0),
    );
  });

  it('el filtro por nivel pendiente está presente', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Todos los niveles')).toBeInTheDocument(),
    );
  });

  it('search.nivelPendiente se propaga al request (query param)', async () => {
    searchHolder.current = { nivelPendiente: 2, offset: 0, limit: 50 };
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(urlVista).toContain('nivelPendiente=2'));
  });
});

describe('<BandejaPendientes> — aprobación masiva', () => {
  const base: Omit<PendienteAutorizacionItemResponse, 'id' | 'folio' | 'nivelPendiente'> = {
    folioAnio: 2026,
    estado: EstadoRequisicion.EnAutorizacion,
    clasificacion: Clasificacion.Servicio,
    prioridad: Prioridad.Normal,
    sucursalId: 's-1',
    departamentoId: 'd-1',
    requisitanteId: 'u-1',
    descripcion: null,
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: null,
    requisitanteNombre: 'Pepe Pérez',
    departamentoNombre: 'Compras',
    departamentoClave: 'COMPRAS',
    situacionSurtido: null,
    origen: OrigenRequisicion.Manual,
    disponibilidad: DisponibilidadPendiente.Total,
    esRecurrente: false,
  };
  const itemN1: PendienteAutorizacionItemResponse = {
    ...base,
    id: 'rq-n1',
    folio: 'MID2026-000101',
    nivelPendiente: NivelAutorizacion.Nivel1,
  };
  const itemN2: PendienteAutorizacionItemResponse = {
    ...base,
    id: 'rq-n2',
    folio: 'MID2026-000102',
    nivelPendiente: NivelAutorizacion.Nivel2,
  };

  it('seleccionar + "Aprobar seleccionadas" envía un POST por nivel y muestra resultado por fila', async () => {
    useAuthStore.setState({
      permisos: [
        'compras.requisiciones.autorizar-nivel1',
        'compras.requisiciones.autorizar-nivel2',
      ],
    });
    const llamadas: { ids: string[]; nivel: number; key: string | null }[] = [];
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({
          items: [itemN1, itemN2],
          offset: 0,
          limit: 50,
          total: 2,
        }),
      ),
      http.post(
        '*/api/v1/compras/requisiciones/autorizaciones-masivas',
        async ({ request }) => {
          const body = (await request.json()) as { ids: string[]; nivel: number };
          llamadas.push({
            ids: body.ids,
            nivel: body.nivel,
            key: request.headers.get('Idempotency-Key'),
          });
          return HttpResponse.json({
            resultados: body.ids.map((id) =>
              id === 'rq-n2'
                ? {
                    id,
                    ok: false,
                    errorCodigo: 'SIN_FIRMA_PREVIA',
                    mensaje: 'Requiere firma de N1 previa',
                  }
                : { id, ok: true, errorCodigo: null, mensaje: null },
            ),
          });
        },
      ),
    );

    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    const checkN1 = (
      await screen.findAllByRole('checkbox', { name: 'Seleccionar MID2026-000101' })
    )[0];
    const checkN2 = screen.getAllByRole('checkbox', {
      name: 'Seleccionar MID2026-000102',
    })[0];
    fireEvent.click(checkN1);
    fireEvent.click(checkN2);
    fireEvent.click(
      screen.getByRole('button', { name: 'Aprobar seleccionadas (2)' }),
    );

    await waitFor(() => expect(llamadas).toHaveLength(2));
    expect(llamadas[0]).toEqual({
      ids: ['rq-n1'],
      nivel: NivelAutorizacion.Nivel1,
      key: expect.any(String),
    });
    expect(llamadas[1]).toEqual({
      ids: ['rq-n2'],
      nivel: NivelAutorizacion.Nivel2,
      key: expect.any(String),
    });

    // El folio también aparece en el resumen; la fila es el ancestro <tr>.
    const filaDe = (folio: string) =>
      screen
        .getAllByText(folio)
        .map((el) => el.closest('tr'))
        .find((tr) => tr != null)!;
    const filaN1 = filaDe('MID2026-000101');
    const filaN2 = filaDe('MID2026-000102');
    expect(await within(filaN1).findByRole('status')).toHaveTextContent('Aprobada');
    expect(await within(filaN2).findByRole('status')).toHaveTextContent(
      'Requiere firma de N1 previa',
    );
    // Las aprobadas salen de la selección; la fallida permanece seleccionada.
    expect(
      screen.getByRole('button', { name: 'Aprobar seleccionadas (1)' }),
    ).toBeInTheDocument();
  });

  it('el resumen de aprobación persiste aunque la lista se refresque sin las filas aprobadas', async () => {
    useAuthStore.setState({
      permisos: [
        'compras.requisiciones.autorizar-nivel1',
        'compras.requisiciones.autorizar-nivel2',
      ],
    });
    let refrescada = false;
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json(
          refrescada
            ? { items: [], offset: 0, limit: 50, total: 0 }
            : { items: [itemN1, itemN2], offset: 0, limit: 50, total: 2 },
        ),
      ),
      http.post(
        '*/api/v1/compras/requisiciones/autorizaciones-masivas',
        async ({ request }) => {
          const body = (await request.json()) as { ids: string[] };
          refrescada = true;
          return HttpResponse.json({
            resultados: body.ids.map((id) =>
              id === 'rq-n2'
                ? {
                    id,
                    ok: false,
                    errorCodigo: 'SIN_FIRMA_PREVIA',
                    mensaje: 'Requiere firma de N1 previa',
                  }
                : { id, ok: true, errorCodigo: null, mensaje: null },
            ),
          });
        },
      ),
    );

    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    fireEvent.click(
      (await screen.findAllByRole('checkbox', { name: 'Seleccionar MID2026-000101' }))[0],
    );
    fireEvent.click(
      screen.getAllByRole('checkbox', { name: 'Seleccionar MID2026-000102' })[0],
    );
    fireEvent.click(
      screen.getByRole('button', { name: 'Aprobar seleccionadas (2)' }),
    );

    // La lista ya no tiene filas; el resumen debe seguir visible.
    await screen.findByText(/no hay requisiciones pendientes/i);
    const resumen = screen.getByRole('status', { name: 'Resumen de aprobación' });
    expect(resumen).toHaveTextContent('1 aprobada · 1 con error');
    expect(within(resumen).getByText('MID2026-000101')).toBeInTheDocument();
    expect(within(resumen).getByText('MID2026-000102')).toBeInTheDocument();
    expect(
      within(resumen).getByText('Requiere firma de N1 previa'),
    ).toBeInTheDocument();

    fireEvent.click(within(resumen).getByRole('button', { name: 'Entendido' }));
    expect(
      screen.queryByRole('status', { name: 'Resumen de aprobación' }),
    ).not.toBeInTheDocument();
  });
});
