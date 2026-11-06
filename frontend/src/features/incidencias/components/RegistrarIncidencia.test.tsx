import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ApiError } from '@/lib/api';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { motivoDesdeError } from '@/features/incidencias/lib/motivo';
import { cerrarSesion, detalle, sesion } from '@/features/incidencias/test-utils';
import type { ContextoIncidencia } from '@/features/incidencias/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
}));

const rechazo = new ApiError(
  {
    type: 'about:blank',
    title: 'Regla de negocio',
    status: 422,
    code: 'ENTRADA_SIN_ASIGNACION',
    detail: 'El artículo no tiene ubicación asignada en este almacén.',
  },
  422,
);

const contexto: ContextoIncidencia = {
  modulo: 'almacen',
  tipoDocumento: 'articulo',
  documentoId: 'art-1',
  documentoEtiqueta: 'ART-0001 · Silicón',
  sucursalId: 'suc-1',
  paso: 'alm.ubicaciones.asignacion',
  pasoReanudacion: 'alm.ubicaciones.asignar',
  rutaReanudacion: '/almacen/recepciones/rec-1',
  responsableUsuarioId: 'jefe-1',
  responsableNombre: 'Jefe de almacén',
};

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [{ id: 'jefe-1', nombre: 'Jefe de almacén', email: 'j@e.com', activo: true }], total: 1 }),
    ),
  );
});

afterEach(cerrarSesion);

describe('motivoDesdeError', () => {
  it('toma el código y el detail del Problem Details', () => {
    expect(motivoDesdeError(rechazo)).toEqual({
      codigoMotivo: 'ENTRADA_SIN_ASIGNACION',
      motivo: 'El artículo no tiene ubicación asignada en este almacén.',
    });
  });

  it('sin ApiError no prellena nada', () => {
    expect(motivoDesdeError(new Error('x'))).toEqual({ codigoMotivo: null, motivo: '' });
  });
});

describe('«Registrar incidencia» en el aviso de error', () => {
  it('sin el permiso de registrar el aviso no muestra el botón', () => {
    sesion([]);
    render(
      <AvisoEnLinea tono="error" incidencia={{ contexto, error: rechazo }}>
        El artículo no tiene ubicación asignada.
      </AvisoEnLinea>,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar incidencia' })).not.toBeInTheDocument();
  });

  it('un aviso de advertencia nunca ofrece registrar', () => {
    sesion(['compartido.incidencias.registrar']);
    render(
      <AvisoEnLinea tono="advertencia" incidencia={{ contexto }}>
        Revisa la ubicación.
      </AvisoEnLinea>,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.queryByRole('button', { name: 'Registrar incidencia' })).not.toBeInTheDocument();
  });

  it('abre prellenado y envía el documento, el paso y el código del rechazo con Idempotency-Key', async () => {
    sesion(['compartido.incidencias.registrar']);
    let cuerpo: Record<string, unknown> | null = null;
    let llave: string | null = null;
    mswServer.use(
      http.post('*/api/v1/incidencias', async ({ request }) => {
        cuerpo = (await request.json()) as Record<string, unknown>;
        llave = request.headers.get('Idempotency-Key');
        return HttpResponse.json(detalle({ numero: 31 }), { status: 201, headers: { ETag: '"1"' } });
      }),
    );
    render(
      <AvisoEnLinea tono="error" incidencia={{ contexto, error: rechazo }}>
        El artículo no tiene ubicación asignada.
      </AvisoEnLinea>,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Registrar incidencia' }));
    const motivo = await screen.findByLabelText(/Motivo/);
    expect(motivo).toHaveValue('El artículo no tiene ubicación asignada en este almacén.');

    fireEvent.click(screen.getAllByRole('button', { name: 'Registrar incidencia' }).at(-1)!);
    await waitFor(() => expect(cuerpo).not.toBeNull());
    expect(cuerpo).toMatchObject({
      modulo: 'almacen',
      tipoDocumento: 'articulo',
      documentoId: 'art-1',
      documentoEtiqueta: 'ART-0001 · Silicón',
      sucursalId: 'suc-1',
      paso: 'alm.ubicaciones.asignacion',
      codigoMotivo: 'ENTRADA_SIN_ASIGNACION',
      motivo: 'El artículo no tiene ubicación asignada en este almacén.',
      responsableUsuarioId: 'jefe-1',
      pasoReanudacion: 'alm.ubicaciones.asignar',
      rutaReanudacion: '/almacen/recepciones/rec-1',
      fechaCompromiso: null,
    });
    expect(llave).toMatch(/^[0-9a-f-]{36}$/);
    await waitFor(() => expect(screen.queryByLabelText(/Motivo/)).not.toBeInTheDocument());
  });

  it('valida motivo mínimo y responsable antes de enviar', async () => {
    sesion(['compartido.incidencias.registrar']);
    const post = vi.fn();
    mswServer.use(http.post('*/api/v1/incidencias', () => { post(); return HttpResponse.json({}); }));
    render(
      <AvisoEnLinea tono="error" incidencia={{ contexto: { ...contexto, responsableUsuarioId: null } }}>
        Falla
      </AvisoEnLinea>,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Registrar incidencia' }));
    await screen.findByLabelText(/Motivo/);
    fireEvent.click(screen.getAllByRole('button', { name: 'Registrar incidencia' }).at(-1)!);
    expect(await screen.findByText('Describe el motivo en al menos 5 caracteres.')).toBeInTheDocument();
    expect(screen.getByText('Elige quién debe atenderla.')).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });
});
