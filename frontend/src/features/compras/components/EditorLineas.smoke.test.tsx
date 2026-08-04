import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EditorLineas } from '@/features/compras/components/EditorLineas';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  EstadoRequisicion,
  Prioridad,
  type LineaResponse,
  type RequisicionResponse,
} from '@/features/compras/api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

function makeLinea(overrides: Partial<LineaResponse> = {}): LineaResponse {
  return {
    id: 'l-1',
    posicion: 1,
    articuloId: 'art-1',
    cantidad: 5,
    unidadMedida: 'PZA',
    precioEstimadoMonto: 100,
    precioEstimadoMoneda: 'MXN',
    cuentaContableId: null,
    centroCostoId: null,
    proyecto: null,
    fechaRequerida: null,
    notas: null,
    cantDeAlmacen: 0,
    cantDeCompra: 0,
    cantRecibida: 0,
    cantPendiente: 5,
    reservaId: null,
    ...overrides,
  };
}

function makeRq(overrides: Partial<RequisicionResponse> = {}): RequisicionResponse {
  return {
    id: 'rq-1',
    empresaId: 'e-1',
    folio: 'MID2026-000001',
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
    version: 1,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
    lineas: [makeLinea()],
    autorizaciones: [],
    ...overrides,
  };
}

beforeEach(() => {
  // Catálogo de motivos para ModalMotivo (si llegara a abrir).
  mswServer.use(
    http.get('*/api/v1/compras/motivos-rechazo', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'u@m.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.ComprasRequisicionesEditar],
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

describe('<EditorLineas> — smoke', () => {
  it('Borrador con líneas: muestra tabla + botón Agregar línea', () => {
    render(<EditorLineas rq={makeRq()} />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('button', { name: /agregar línea/i }),
    ).toBeInTheDocument();
    // Cantidad de la línea visible en la tabla.
    expect(screen.getByText(/PZA/)).toBeInTheDocument();
  });

  it('0 líneas + permiso agregar: muestra EmptyState con CTA', () => {
    render(<EditorLineas rq={makeRq({ lineas: [] })} />, {
      wrapper: createQueryWrapper(),
    });
    expect(
      screen.getByRole('button', { name: /agregar línea/i }),
    ).toBeInTheDocument();
  });

  it('estado terminal (Cerrada) sin permisos: read-only, sin acciones', () => {
    render(
      <EditorLineas
        rq={makeRq({ estado: EstadoRequisicion.Cerrada })}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.queryByRole('button', { name: /agregar línea/i }),
    ).not.toBeInTheDocument();
  });
});
