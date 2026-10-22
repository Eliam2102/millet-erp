import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CabeceraRequisicion } from '@/features/compras/components/CabeceraRequisicion';
import {
  Clasificacion,
  EstadoRequisicion,
  Prioridad,
  type RequisicionResponse,
  OrigenRequisicion,
} from '@/features/compras/api/types';

function makeRq(overrides: Partial<RequisicionResponse> = {}): RequisicionResponse {
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
    requisitanteNombre: 'Pedro García',
    departamentoNombre: 'Compras y Adquisiciones',
    departamentoClave: 'COMPRAS',
    descripcion: 'Servicio de mantenimiento',
    prioridad: Prioridad.Alta,
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: '2026-05-15',
    proveedorSugeridoId: null,
    estado: EstadoRequisicion.EnAutorizacion,
    motivoTerminacionId: null,
    motivoTerminacionTexto: null,
    actorTerminacionId: null,
    fechaTerminacion: null,
    version: 12,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
    lineas: [],
    autorizaciones: [],
    proveedorSugeridoRazonSocial: null,
    proveedorSugeridoClave: null,
    comprometidaEnOcId: null,
    situacionSurtido: null,
    origen: OrigenRequisicion.Manual,
    ...overrides,
  };
}

const resolverIdentidad = (id: string | null | undefined) =>
  id == null ? '—' : `[${id}]`;

describe('<CabeceraRequisicion>', () => {
  it('muestra folio, estado y datos de cabecera resueltos', () => {
    render(
      <CabeceraRequisicion rq={makeRq()} resolverNombre={resolverIdentidad} />,
    );
    // El folio y el estado viven en el sub-topbar: la cabecera no tiene heading.
    expect(screen.queryByRole('heading')).not.toBeInTheDocument();
    expect(screen.getByText('Alta')).toBeInTheDocument();
    expect(screen.getByText('Servicio')).toBeInTheDocument();
    // Requisitante y departamento vienen resueltos del backend (ADR-0042),
    // NO del resolverNombre. Departamento como "clave · nombre".
    expect(screen.getByText('Pedro García')).toBeInTheDocument(); // requisitante (backend)
    expect(
      screen.getByText('COMPRAS · Compras y Adquisiciones'),
    ).toBeInTheDocument(); // depto (backend)
    // Sucursal sigue resolviéndose client-side vía resolverNombre.
    expect(screen.getByText('[s-1]')).toBeInTheDocument();
    expect(screen.getByText('v12')).toBeInTheDocument(); // version
  });

  it('muestra descripción cuando está presente', () => {
    render(
      <CabeceraRequisicion
        rq={makeRq({ descripcion: 'Una descripción específica' })}
        resolverNombre={resolverIdentidad}
      />,
    );
    expect(screen.getByText('Una descripción específica')).toBeInTheDocument();
  });

  it('muestra bloque de motivo de terminación cuando está cancelada/rechazada', () => {
    render(
      <CabeceraRequisicion
        rq={makeRq({
          estado: EstadoRequisicion.Cancelada,
          motivoTerminacionTexto: 'Cancelada por proveedor cambiado',
          fechaTerminacion: '2026-05-10T14:00:00Z',
        })}
        resolverNombre={resolverIdentidad}
      />,
    );
    expect(screen.getByText('Motivo de terminación')).toBeInTheDocument();
    expect(
      screen.getByText('Cancelada por proveedor cambiado'),
    ).toBeInTheDocument();
  });

  it('proveedor sugerido en null muestra "—"', () => {
    render(
      <CabeceraRequisicion
        rq={makeRq({ proveedorSugeridoId: null })}
        resolverNombre={resolverIdentidad}
      />,
    );
    // Hay varios "—" potenciales (empty values); aseguremos que el bloque
    // "Proveedor sugerido" exista.
    expect(screen.getByText('Proveedor sugerido')).toBeInTheDocument();
  });

  it('muestra el surtido de almacén (Parcial o Completo)', () => {
    const { rerender } = render(<CabeceraRequisicion rq={makeRq()} resolverNombre={resolverIdentidad} />);
    expect(screen.getByText('Surtido de almacén')).toBeInTheDocument();
    expect(screen.getByText('Parcial')).toBeInTheDocument();
    rerender(<CabeceraRequisicion rq={makeRq({ soloApartarSiCompleto: true })} resolverNombre={resolverIdentidad} />);
    expect(screen.getByText('Completo')).toBeInTheDocument();
  });

  describe('editar cabecera', () => {
    afterEach(() => useAuthStore.setState({ permisos: [] }));

    function renderBorrador(permisos: string[], estado: EstadoRequisicion = EstadoRequisicion.Borrador) {
      useAuthStore.setState({ permisos });
      render(
        <CabeceraRequisicion rq={makeRq({ estado, obra: 'Obra vieja' })} resolverNombre={resolverIdentidad} />,
        { wrapper: createQueryWrapper() },
      );
    }

    it('sin permiso o fuera de Borrador no ofrece editar', () => {
      renderBorrador([]);
      expect(screen.queryByRole('button', { name: 'Editar cabecera' })).toBeNull();
    });

    it('fuera de Borrador no ofrece editar aunque haya permiso', () => {
      renderBorrador([PermisosCanonicos.ComprasRequisicionesEditar], EstadoRequisicion.Autorizada);
      expect(screen.queryByRole('button', { name: 'Editar cabecera' })).toBeNull();
    });

    it('envía solo lo que cambió con Idempotency-Key', async () => {
      let body: unknown = null;
      let key: string | null = null;
      mswServer.use(
        http.patch('*/api/v1/compras/requisiciones/rq-1', async ({ request }) => {
          body = await request.json();
          key = request.headers.get('Idempotency-Key');
          return new HttpResponse(null, { status: 204 });
        }),
      );
      renderBorrador([PermisosCanonicos.ComprasRequisicionesEditar]);
      fireEvent.click(screen.getByRole('button', { name: 'Editar cabecera' }));
      fireEvent.change(screen.getByLabelText(/Obra/), { target: { value: '  Torre Norte ' } });
      fireEvent.click(screen.getByLabelText(/Completo: si no alcanza/));
      fireEvent.click(screen.getByRole('button', { name: 'Guardar cabecera' }));
      await waitFor(() =>
        expect(body).toEqual({ obra: 'Torre Norte', soloApartarSiCompleto: true }),
      );
      expect(key).toBeTruthy();
      await waitFor(() => expect(screen.queryByRole('form', { name: 'Editar cabecera' })).toBeNull());
    });

    it('vaciar la obra manda limpiarObra y un 422 se muestra sin cerrar', async () => {
      let body: unknown = null;
      mswServer.use(
        http.patch('*/api/v1/compras/requisiciones/rq-1', async ({ request }) => {
          body = await request.json();
          return HttpResponse.json(
            { type: 'about:blank', title: 'Regla', status: 422, code: 'RQ_NO_EDITABLE', detail: 'Ya no está en Borrador.' },
            { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
          );
        }),
      );
      renderBorrador([PermisosCanonicos.ComprasRequisicionesEditar]);
      fireEvent.click(screen.getByRole('button', { name: 'Editar cabecera' }));
      fireEvent.change(screen.getByLabelText(/Obra/), { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: 'Guardar cabecera' }));
      await waitFor(() => expect(body).toEqual({ limpiarObra: true }));
      expect(screen.getByRole('form', { name: 'Editar cabecera' })).toBeInTheDocument();
    });
  });
});
