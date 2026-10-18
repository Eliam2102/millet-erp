import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { partida, poliza } from '../lib/__fixtures__/polizas-fix';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...rest }: { children: React.ReactNode; to: string; params?: { id: string }; [k: string]: unknown }) => {
    const attrs: Record<string, unknown> = { ...rest };
    delete attrs.search;
    return <a href={params ? to.replace('$id', params.id) : to} {...(attrs as object)}>{children}</a>;
  },
}));

import { PolizaDetallePage } from './PolizaDetallePage';

/** K10.4: reclasificación ligada, reversas de recurrentes y sección «Pólizas ligadas» en el detalle de póliza. */
const API = '*/api/v1/contabilidad';
const problem = (status: number, body: Record<string, unknown>) =>
  HttpResponse.json({ title: 'x', status, ...body }, { status, headers: { 'Content-Type': 'application/problem+json' } });

function servidor(...ps: ReturnType<typeof poliza>[]) {
  mswServer.use(
    http.get(`${API}/polizas/:id/adjuntos`, () => HttpResponse.json([])),
    http.get('*/api/v1/adjuntos/tipos', () => HttpResponse.json([])),
    ...ps.map((p) => http.get(`${API}/polizas/${p.id}`, () => HttpResponse.json(p, { headers: { ETag: `"${p.version}"` } }))),
    http.get(`${API}/polizas`, () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 50 })),
    http.get(`${API}/tipos-documento`, () => HttpResponse.json([
      { id: 't1', clave: 'TT', nombre: 'FIX Traspasos', activo: true, esPrueba: false, version: 1, tipoSat: 'Diario' },
      { id: 't-as', clave: 'AS', nombre: 'FIX Asiento contable', activo: true, esPrueba: false, version: 1, tipoSat: 'Diario' },
    ])),
    http.get(`${API}/movimientos/sucursales`, () => HttpResponse.json([{ id: 's1', clave: 'MER', nombre: 'FIX Mérida', activa: true }])),
  );
}

function permisos(...extra: string[]) {
  useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPolizasLeer, ...extra] });
}

afterEach(() => useAuthStore.setState({ permisos: [], user: null }));

describe('<PolizaDetallePage> · K10.4', () => {
  it('la original lista sus pólizas ligadas (reversa y reclasificación) y enlaza a la plantilla que la generó', async () => {
    servidor(poliza({
      plantillaRecurrenteId: 'r1', plantillaRecurrenteNombre: 'FIX Provisión de renta',
      ligadas: [
        { id: 'p11', folio: 'TT-000131', relacion: 'Reversa', estado: 'Autorizada', fecha: '2026-11-01', concepto: 'Reversa de TT-000123' },
        { id: 'p12', folio: null, relacion: 'Reclasificacion', estado: 'Borrador', fecha: '2026-10-20', concepto: 'Reclasificación de TT-000123' },
      ],
    }));
    permisos();
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });

    const nota = await screen.findByRole('note', { name: 'Plantilla recurrente' });
    expect(within(nota).getByRole('link', { name: '«FIX Provisión de renta»' })).toHaveAttribute('href', '/contabilidad/polizas/recurrentes/r1');
    expect(screen.getAllByText('Recurrente').length).toBeGreaterThan(0);

    const ligadas = screen.getByRole('region', { name: 'Pólizas ligadas' });
    const filas = within(ligadas).getAllByRole('row').slice(1);
    expect(filas).toHaveLength(2);
    expect(within(filas[0]).getByText('Reversa')).toBeInTheDocument();
    expect(within(filas[0]).getByRole('link', { name: 'TT-000131' })).toHaveAttribute('href', '/contabilidad/polizas/p11');
    expect(within(filas[0]).getByText('1 nov 2026')).toBeInTheDocument();
    expect(within(filas[1]).getByText('Reclasificación')).toBeInTheDocument();
    expect(within(filas[1]).getByRole('link', { name: 'Borrador' })).toHaveAttribute('href', '/contabilidad/polizas/p12');
  });

  it('la reversa muestra «Reversa de» con liga a la original y sus importes en negativo', async () => {
    servidor(poliza({
      id: 'p11', folio: 'TT-000131', concepto: 'Reversa de TT-000123', relacion: 'Reversa', polizaRelacionadaId: 'p1',
      polizaRelacionadaFolio: 'TT-000123', plantillaRecurrenteId: 'r1', plantillaRecurrenteNombre: 'FIX Provisión de renta',
      totalCargos: -1234, totalAbonos: -1234,
      partidas: [partida({ cargo: -1234 }), partida({ id: 'pa2', linea: 2, cuentaCodigo: 'FIX-120', cargo: 0, abono: -1234 })],
    }));
    permisos();
    render(<PolizaDetallePage id="p11" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });
    const nota = await screen.findByRole('note', { name: 'Póliza revertida' });
    expect(nota).toHaveTextContent('Reversa de TT-000123');
    expect(within(nota).getByRole('link', { name: 'TT-000123' })).toHaveAttribute('href', '/contabilidad/polizas/p1');
    expect(screen.getAllByText('Reversa').length).toBeGreaterThan(0);
  });

  it('la reclasificación muestra «Reclasifica a» con liga a la original', async () => {
    servidor(poliza({ id: 'p12', folio: 'AS-000007', relacion: 'Reclasificacion', polizaRelacionadaId: 'p1', polizaRelacionadaFolio: 'TT-000123' }));
    permisos();
    render(<PolizaDetallePage id="p12" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });
    const nota = await screen.findByRole('note', { name: 'Póliza reclasificada' });
    expect(nota).toHaveTextContent('Reclasifica a TT-000123');
    expect(within(nota).getByRole('link', { name: 'TT-000123' })).toHaveAttribute('href', '/contabilidad/polizas/p1');
  });

  it('«Reclasificar» abre la nueva póliza con el aviso, tipo AS y concepto sugerido, y envía reclasificaPolizaId', async () => {
    servidor(poliza());
    const cuerpos: Record<string, unknown>[] = [];
    mswServer.use(http.post(`${API}/polizas`, async ({ request }) => {
      cuerpos.push((await request.json()) as Record<string, unknown>);
      return HttpResponse.json(poliza({ id: 'p12', folio: null, estado: 'Borrador', relacion: 'Reclasificacion', polizaRelacionadaId: 'p1' }), { status: 201 });
    }));
    const onNavegar = vi.fn();
    permisos(PermisosCanonicos.ContabilidadPolizasCapturar);
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={onNavegar} />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: 'Reclasificar' }));
    const hoja = await screen.findByRole('dialog');
    expect(within(hoja).getByRole('note')).toHaveTextContent('Reclasifica a TT-000123');
    expect(within(hoja).getByLabelText('Concepto', { selector: '#pol-concepto' })).toHaveValue('Reclasificación de TT-000123');
    await within(hoja).findByRole('option', { name: 'AS — FIX Asiento contable (Diario)' });
    expect(within(hoja).getByLabelText('Tipo de operación')).toHaveValue('t-as');

    fireEvent.click(within(hoja).getByRole('button', { name: 'Guardar borrador' }));
    await waitFor(() => expect(onNavegar).toHaveBeenCalledWith('p12'));
    expect(cuerpos[0]).toMatchObject({ reclasificaPolizaId: 'p1', tipoOperacionId: 't-as', concepto: 'Reclasificación de TT-000123' });
  });

  it('anular una póliza con reversa vigente muestra el rechazo del servidor', async () => {
    servidor(poliza());
    mswServer.use(http.post(`${API}/polizas/p1/anular`, () =>
      problem(422, { code: 'CONTAB_POLIZA_TIENE_REVERSA', detail: 'La póliza tiene una reversa automática vigente (TT-000131): anula primero la reversa.' })));
    permisos(PermisosCanonicos.ContabilidadPolizasAnular);
    render(<PolizaDetallePage id="p1" search={{}} onNavegar={vi.fn()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Anular' }));
    const dialogo = await screen.findByRole('dialog');
    fireEvent.change(within(dialogo).getByLabelText('Motivo'), { target: { value: 'FIX Provisión duplicada' } });
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Anular póliza' }));
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('anula primero la reversa');
  });
});
