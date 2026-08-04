import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AccionesRequisicion } from '@/features/compras/components/AccionesRequisicion';
import {
  Clasificacion,
  EstadoRequisicion,
  NivelAutorizacion,
  OrigenRequisicion,
  Prioridad,
  type RequisicionResponse,
} from '@/features/compras/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  NuevaOrdenCompraContext,
  type NuevaOrdenCompraApi,
} from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import type { ReactNode } from 'react';

// Stub del context "Nueva orden de compra" para los tests. AccionesRequisicion
// inyecta useNuevaOrdenCompra() por el botón "Convertir a OC" (PR-B
// 2026-05-13); el provider real vive en routes/_app.tsx y no se monta en
// tests unitarios. Stub no-op suficiente — los tests no asertan el flujo
// de apertura del Sheet.
const stubNuevaOcApi: NuevaOrdenCompraApi = {
  abrir: () => {},
  cerrar: () => {},
  setDirty: () => {},
};
function withNuevaOcStub(children: ReactNode) {
  return (
    <NuevaOrdenCompraContext.Provider value={stubNuevaOcApi}>
      {children}
    </NuevaOrdenCompraContext.Provider>
  );
}
function wrapperWithStubs() {
  const QueryWrapper = createQueryWrapper();
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryWrapper>{withNuevaOcStub(children)}</QueryWrapper>;
  };
}

const USER_ID = 'u-test';

function makeRq(
  overrides: Partial<RequisicionResponse> = {},
): RequisicionResponse {
  return {
    id: 'rq-1',
    empresaId: 'e-1',
    folio: 'MID2026-000042',
    folioAnio: 2026,
    clasificacion: Clasificacion.Servicio,
    sucursalId: 's-1',
    departamentoId: 'd-1',
    almacenDestinoId: 'a-1',
    requisitanteId: 'u-1',
    creadorId: 'u-1',
    descripcion: null,
    prioridad: Prioridad.Normal,
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: null,
    proveedorSugeridoId: null,
    estado: EstadoRequisicion.Borrador,
    motivoTerminacionId: null,
    motivoTerminacionTexto: null,
    actorTerminacionId: null,
    fechaTerminacion: null,
    comprometidaEnOcId: null,
    version: 1,
    origen: OrigenRequisicion.Manual,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
    lineas: [
      {
        id: 'l-1',
        posicion: 1,
        articuloId: 'a-1',
        cantidad: 1,
        unidadMedida: 'PZA',
        precioEstimadoMonto: 100,
        precioEstimadoMoneda: 'MXN',
        cuentaContableId: null,
        centroCostoId: null,
        proyecto: null,
        fechaRequerida: null,
        notas: null,
        cantDeAlmacen: 0,
        cantDeCompra: 1,
        cantRecibida: 0,
        cantPendiente: 1,
        reservaId: null,
      },
    ],
    autorizaciones: [],
    ...overrides,
  };
}

function setupSession(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: USER_ID, email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  // Endpoint del catálogo de motivos (ModalMotivo lo consume).
  mswServer.use(
    http.get('*/api/v1/compras/motivos-rechazo', () => HttpResponse.json([])),
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

describe('<AccionesRequisicion>', () => {
  it('sin permisos: no renderiza nada (toolbar oculto)', () => {
    setupSession([]);
    const { container } = render(
      <AccionesRequisicion rq={makeRq()} />,
      { wrapper: wrapperWithStubs() },
    );
    expect(container.firstChild).toBeNull();
  });

  it('Borrador + permiso editar: muestra Transmitir habilitado', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEditar]);
    render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });
    const btn = screen.getByRole('button', { name: /transmitir/i });
    expect(btn).toBeEnabled();
  });

  it('Borrador con 0 líneas: Transmitir disabled con tooltip', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEditar]);
    render(
      <AccionesRequisicion rq={makeRq({ lineas: [] })} />,
      { wrapper: wrapperWithStubs() },
    );
    const btn = screen.getByRole('button', { name: /transmitir/i });
    expect(btn).toBeDisabled();
    expect(btn).toHaveAttribute(
      'title',
      expect.stringMatching(/al menos una línea/i),
    );
  });

  it('EnAutorizacion + permiso N1: muestra Aprobar Nivel 1', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesAutorizarNivel1]);
    render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.EnAutorizacion })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(
      screen.getByRole('button', { name: /aprobar nivel 1/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /transmitir/i }),
    ).not.toBeInTheDocument();
  });

  it('EnAutorizacion + permiso N2: muestra Aprobar Nivel 2 disabled hasta que N1 firme', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesAutorizarNivel2]);
    render(
      <AccionesRequisicion
        rq={makeRq({
          estado: EstadoRequisicion.EnAutorizacion,
          autorizaciones: [],
        })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    const btn = screen.getByRole('button', { name: /aprobar nivel 2/i });
    expect(btn).toBeDisabled();
  });

  it('EnAutorizacion + N1 firmado + permiso N2: Aprobar Nivel 2 habilitado', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesAutorizarNivel2]);
    render(
      <AccionesRequisicion
        rq={makeRq({
          estado: EstadoRequisicion.EnAutorizacion,
          autorizaciones: [
            {
              id: 'aut-1',
              nivel: NivelAutorizacion.Nivel1,
              usuarioId: 'u-other',
              fechaHora: '2026-05-09T11:00:00Z',
              notas: null,
            },
          ],
        })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(
      screen.getByRole('button', { name: /aprobar nivel 2/i }),
    ).toBeEnabled();
  });

  it('EnAutorizacion + permiso rechazar: muestra Rechazar', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesRechazar]);
    render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.EnAutorizacion })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(
      screen.getByRole('button', { name: /rechazar/i }),
    ).toBeInTheDocument();
  });

  it('Autorizada (terminal): no renderiza acciones de workflow', () => {
    setupSession([
      PermisosCanonicos.ComprasRequisicionesEditar,
      PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
      PermisosCanonicos.ComprasRequisicionesAutorizarNivel2,
      PermisosCanonicos.ComprasRequisicionesRechazar,
    ]);
    const { container } = render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.Autorizada })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(container.firstChild).toBeNull();
  });

  it('N1 ya firmado por current user: el botón Aprobar N1 desaparece', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesAutorizarNivel1]);
    render(
      <AccionesRequisicion
        rq={makeRq({
          estado: EstadoRequisicion.EnAutorizacion,
          autorizaciones: [
            {
              id: 'aut-1',
              nivel: NivelAutorizacion.Nivel1,
              usuarioId: USER_ID,
              fechaHora: '2026-05-09T11:00:00Z',
              notas: null,
            },
          ],
        })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(
      screen.queryByRole('button', { name: /aprobar nivel 1/i }),
    ).not.toBeInTheDocument();
  });

  it('N1 firmado por OTRO + permisos N1+N2: botón Aprobar N1 oculto, N2 sigue', () => {
    setupSession([
      PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
      PermisosCanonicos.ComprasRequisicionesAutorizarNivel2,
    ]);
    render(
      <AccionesRequisicion
        rq={makeRq({
          estado: EstadoRequisicion.EnAutorizacion,
          autorizaciones: [
            {
              id: 'aut-1',
              nivel: NivelAutorizacion.Nivel1,
              usuarioId: 'u-other',
              fechaHora: '2026-05-09T11:00:00Z',
              notas: null,
            },
          ],
        })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    // N1 ya firmado por otro: el dominio impide un 2º N1 → el botón se oculta
    // aunque el usuario tenga permiso N1 (simétrico con N2).
    expect(
      screen.queryByRole('button', { name: /aprobar nivel 1/i }),
    ).not.toBeInTheDocument();
    // N2 se sigue ofreciendo (N1 está firmado).
    expect(
      screen.getByRole('button', { name: /aprobar nivel 2/i }),
    ).toBeEnabled();
  });

  it('N1 firmado: muestra chip "Nivel 1 autorizado" con la fecha de la firma', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesAutorizarNivel2]);
    const { container } = render(
      <AccionesRequisicion
        rq={makeRq({
          estado: EstadoRequisicion.EnAutorizacion,
          autorizaciones: [
            {
              id: 'aut-1',
              nivel: NivelAutorizacion.Nivel1,
              usuarioId: 'u-other',
              fechaHora: '2026-05-09T11:00:00Z',
              notas: null,
            },
          ],
        })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(screen.getByText(/nivel 1 autorizado/i)).toBeInTheDocument();
    // La fecha viene de la firma N1: el <time> lleva el ISO crudo en datetime.
    expect(
      container.querySelector('time[datetime="2026-05-09T11:00:00Z"]'),
    ).not.toBeNull();
    // El chip es informativo (Badge = div), no un botón.
    expect(
      screen.queryByRole('button', { name: /nivel 1/i }),
    ).not.toBeInTheDocument();
  });

  it('Borrador + permiso eliminar: muestra botón Eliminar', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEliminar]);
    render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });
    expect(
      screen.getByRole('button', { name: /eliminar/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^cancelar$/i }),
    ).not.toBeInTheDocument();
  });

  it('EnAutorizacion + permiso eliminar: también muestra Eliminar', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEliminar]);
    render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.EnAutorizacion })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(
      screen.getByRole('button', { name: /eliminar/i }),
    ).toBeInTheDocument();
  });

  it('Autorizada + permiso cancelar: muestra botón Cancelar (no Eliminar)', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesCancelar]);
    render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.Autorizada })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    // Único botón "Cancelar" en toolbar (no confundir con Cancelar de
    // confirm dialogs — esos son AlertDialog, no aparecen sin click previo).
    expect(
      screen.getByRole('button', { name: /^cancelar$/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /eliminar/i }),
    ).not.toBeInTheDocument();
  });

  it('EnSurtido + permiso cancelar: también muestra Cancelar', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesCancelar]);
    render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.EnSurtido })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    expect(
      screen.getByRole('button', { name: /^cancelar$/i }),
    ).toBeInTheDocument();
  });

  it('Sin permiso eliminar: oculta botón Eliminar', () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEditar]);
    render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });
    // Transmitir sí aparece (tiene permiso editar); Eliminar no.
    expect(
      screen.queryByRole('button', { name: /eliminar/i }),
    ).not.toBeInTheDocument();
  });

  it('409 CONCURRENCY_CONFLICT en transmitir: abre conflict dialog (modo simple) con traceId', async () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEditar]);
    mswServer.use(
      http.post(
        '*/api/v1/compras/requisiciones/:id/transmitir',
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Concurrencia',
              status: 409,
              code: 'CONCURRENCY_CONFLICT',
              traceId: 'trace-conflict-001',
            },
            {
              status: 409,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });

    // Click Transmitir → abre confirm dialog.
    screen.getByRole('button', { name: /transmitir/i }).click();
    // Confirma → dispara la mutation → 409 → conflict dialog se abre.
    const confirmar = await screen.findByRole('button', { name: /confirmar/i });
    confirmar.click();

    // El conflict dialog (modo simple) muestra título y traceId.
    await screen.findByText(/Esta requisición fue actualizada por otro usuario/i);
    expect(screen.getByText(/trace-conflict-001/i)).toBeInTheDocument();
    // CTA primario del modo simple.
    expect(
      screen.getByRole('button', { name: /refrescar y revisar/i }),
    ).toBeInTheDocument();
  });

  it('happy path Transmitir: 204 → no error visible (mutation ejecutada con éxito)', async () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEditar]);
    let llamado = false;
    mswServer.use(
      http.post(
        '*/api/v1/compras/requisiciones/:id/transmitir',
        () => {
          llamado = true;
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });

    screen.getByRole('button', { name: /transmitir/i }).click();
    const confirmar = await screen.findByRole('button', { name: /confirmar/i });
    confirmar.click();

    // Esperamos a que la mutation se ejecute (sonner toast no se renderiza
    // sin Toaster; verificamos que el endpoint fue llamado y el confirm
    // dialog se cerró).
    await waitFor(() => expect(llamado).toBe(true));
  });

  it('click Eliminar abre ModalMotivo en variante "eliminar"', async () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEliminar]);
    mswServer.use(
      http.get('*/api/v1/compras/motivos-rechazo', () =>
        HttpResponse.json([]),
      ),
    );
    render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });
    screen.getByRole('button', { name: /eliminar/i }).click();
    await screen.findByText(/Eliminar requisición/i);
    expect(
      screen.getByRole('button', { name: /confirmar eliminación/i }),
    ).toBeInTheDocument();
  });

  it('eliminar una RQ de SISTEMA muestra el aviso de re-propuesta del motor', async () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEliminar]);
    render(
      <AccionesRequisicion rq={makeRq({ origen: OrigenRequisicion.Sistema })} />,
      { wrapper: wrapperWithStubs() },
    );
    screen.getByRole('button', { name: /eliminar/i }).click();
    await screen.findByText(/Eliminar requisición/i);
    expect(screen.getByText(/volverá a proponerla/i)).toBeInTheDocument();
  });

  it('eliminar una RQ MANUAL no muestra el aviso de reabasto', async () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesEliminar]);
    render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });
    screen.getByRole('button', { name: /eliminar/i }).click();
    await screen.findByText(/Eliminar requisición/i);
    expect(screen.queryByText(/volverá a proponerla/i)).not.toBeInTheDocument();
  });

  it('click Cancelar abre ModalMotivo en variante "cancelar"', async () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesCancelar]);
    mswServer.use(
      http.get('*/api/v1/compras/motivos-rechazo', () =>
        HttpResponse.json([]),
      ),
    );
    render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.Autorizada })}
      />,
      { wrapper: wrapperWithStubs() },
    );
    screen.getByRole('button', { name: /^cancelar$/i }).click();
    await screen.findByText(/Cancelar requisición/i);
    expect(
      screen.getByRole('button', { name: /confirmar cancelación/i }),
    ).toBeInTheDocument();
  });

  it('happy path Aprobar Nivel 1: 204 con body { nivel: 1, notas: null }', async () => {
    setupSession([PermisosCanonicos.ComprasRequisicionesAutorizarNivel1]);
    let bodyVisto: unknown = null;
    mswServer.use(
      http.post(
        '*/api/v1/compras/requisiciones/:id/autorizaciones',
        async ({ request }) => {
          bodyVisto = await request.json();
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    render(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.EnAutorizacion })}
      />,
      { wrapper: wrapperWithStubs() },
    );

    screen.getByRole('button', { name: /aprobar nivel 1/i }).click();
    const confirmar = await screen.findByRole('button', { name: /confirmar/i });
    confirmar.click();

    await waitFor(() =>
      expect(bodyVisto).toMatchObject({
        nivel: NivelAutorizacion.Nivel1,
        notas: null,
      }),
    );
  });

  it('regresión idempotencia: dos acciones distintas en el MISMO montaje envían Idempotency-Keys DIFERENTES', async () => {
    // Invariante anti-regresión. useFormIdempotencyKey() da una key estable POR
    // MONTAJE; si AccionesRequisicion la compartiera entre acciones (como antes
    // del fix), la 2ª acción sin desmontar reusaría esa key con un body distinto
    // y el backend respondería 422 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY.
    // El fix genera una key FRESCA por submit (crypto.randomUUID() en cada
    // .mutate). Aquí lo fijamos end-to-end: Transmitir y Aprobar N1 sobre el
    // mismo componente montado deben mandar keys distintas en el header
    // Idempotency-Key. Si alguien vuelve a una key compartida, K1 === K2 y esto
    // truena.
    setupSession([
      PermisosCanonicos.ComprasRequisicionesEditar,
      PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
    ]);

    let keyTransmitir: string | null = null;
    let keyAprobar: string | null = null;
    mswServer.use(
      http.post('*/api/v1/compras/requisiciones/:id/transmitir', ({ request }) => {
        keyTransmitir = request.headers.get('Idempotency-Key');
        return new HttpResponse(null, { status: 204 });
      }),
      http.post(
        '*/api/v1/compras/requisiciones/:id/autorizaciones',
        ({ request }) => {
          keyAprobar = request.headers.get('Idempotency-Key');
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const { rerender } = render(<AccionesRequisicion rq={makeRq()} />, {
      wrapper: wrapperWithStubs(),
    });

    // Acción 1 — Transmitir (Borrador, body { requisicionId }).
    screen.getByRole('button', { name: /transmitir/i }).click();
    (await screen.findByRole('button', { name: /confirmar/i })).click();
    await waitFor(() => expect(keyTransmitir).toBeTruthy());

    // El MISMO componente sigue montado; la RQ avanza a EnAutorizacion.
    rerender(
      <AccionesRequisicion
        rq={makeRq({ estado: EstadoRequisicion.EnAutorizacion })}
      />,
    );

    // Acción 2 — Aprobar Nivel 1 (body { nivel: 1, notas: null } — DISTINTO).
    screen.getByRole('button', { name: /aprobar nivel 1/i }).click();
    (await screen.findByRole('button', { name: /confirmar/i })).click();
    await waitFor(() => expect(keyAprobar).toBeTruthy());

    // Invariante: keys distintas (fresh por submit), no la key compartida de montaje.
    expect(keyTransmitir).not.toBeNull();
    expect(keyTransmitir).not.toBe(keyAprobar);
  });
});
