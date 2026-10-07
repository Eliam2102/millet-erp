import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PeriodosPage } from './PeriodosPage';

const API = '*/api/v1/contabilidad/periodos';

const periodo = (numero: number, estado: 'Abierto' | 'Cerrado' = 'Abierto') => ({
  id: `p${numero}`, ejercicio: 2026, numero, etiqueta: '', esPeriodoAjustes: numero === 13, estado,
  fechaInicio: numero === 13 ? null : `2026-${String(numero).padStart(2, '0')}-01`,
  fechaFin: numero === 13 ? null : `2026-${String(numero).padStart(2, '0')}-28`,
  cerradoPor: estado === 'Cerrado' ? 'FIX Contador' : null, cerradoEn: estado === 'Cerrado' ? '2026-10-06T18:00:00Z' : null,
  reabiertoPor: null, reabiertoEn: null, version: 3,
});
const PERIODOS = [periodo(1, 'Cerrado'), ...Array.from({ length: 12 }, (_, i) => periodo(i + 2))];

function conPermisos(...permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'Test' }, empresas: [], currentEmpresaId: 'e', permisos, errorMessage: null,
  });
}

function servidor(ejercicios: number[] = [2026]) {
  const acciones: { url: string; ifMatch: string | null; body: unknown }[] = [];
  mswServer.use(
    http.get(`${API}/ejercicios`, () => HttpResponse.json(ejercicios)),
    http.get(`${API}/p1/historial`, () => HttpResponse.json([
      { id: 'e2', accion: 'Cerrado', usuario: 'FIX Contador', fecha: '2026-10-06T18:00:00Z', motivo: 'Cierre de enero' },
      { id: 'e1', accion: 'Creado', usuario: 'FIX Admin', fecha: '2026-10-01T15:00:00Z', motivo: null },
    ])),
    http.get(API, () => HttpResponse.json(ejercicios.length ? PERIODOS : [])),
    http.post(`${API}/:id/:accion`, async ({ request, params }) => {
      acciones.push({ url: `${params.id}/${params.accion}`, ifMatch: request.headers.get('If-Match'), body: await request.json() });
      return HttpResponse.json({ ...PERIODOS[0], estado: 'Abierto', version: 4 });
    }),
  );
  return acciones;
}

afterEach(() => useAuthStore.setState({ status: 'idle', accessToken: null, expiresAt: null, user: null, empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null }));

describe('<PeriodosPage>', () => {
  it('muestra los 13 periodos del ejercicio con su estado y quién cerró', async () => {
    conPermisos('contabilidad.periodo.leer');
    servidor();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('01 · Enero')).toBeInTheDocument();
    expect(screen.getByText('Periodo 13 · ajustes')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(14);
    expect(screen.getByText('Cerrado')).toBeInTheDocument();
    expect(screen.getByText(/FIX Contador ·/)).toBeInTheDocument();
    // Solo lectura: ni cerrar, ni reabrir, ni crear ejercicio.
    expect(screen.queryByRole('button', { name: /^Cerrar / })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Reabrir / })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Crear ejercicio' })).not.toBeInTheDocument();
  });

  it('quien cierra pero no reabre solo ve «Cerrar» en los abiertos (C1.1-a)', async () => {
    conPermisos('contabilidad.periodo.leer', 'contabilidad.periodo.cerrar');
    servidor();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('button', { name: 'Cerrar 02 · Febrero' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cerrar 01 · Enero' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reabrir 01 · Enero' })).not.toBeInTheDocument();
  });

  it('reabrir exige motivo y envía la versión vista en If-Match', async () => {
    conPermisos('contabilidad.periodo.leer', 'contabilidad.periodo.reabrir');
    const acciones = servidor();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Reabrir 01 · Enero' }));
    const dialogo = await screen.findByRole('dialog');
    const confirmar = within(dialogo).getByRole('button', { name: 'Reabrir periodo' });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialogo).getByLabelText('Motivo (obligatorio)'), { target: { value: 'Ajuste de provisión' } });
    fireEvent.click(confirmar);
    await waitFor(() => expect(acciones).toHaveLength(1));
    expect(acciones[0]).toEqual({ url: 'p1/reabrir', ifMatch: '"3"', body: { motivo: 'Ajuste de provisión' } });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('cerrar no exige motivo', async () => {
    conPermisos('contabilidad.periodo.leer', 'contabilidad.periodo.cerrar');
    const acciones = servidor();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Cerrar 02 · Febrero' }));
    const dialogo = await screen.findByRole('dialog');
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cerrar periodo' }));
    await waitFor(() => expect(acciones).toHaveLength(1));
    expect(acciones[0]).toEqual({ url: 'p2/cerrar', ifMatch: '"3"', body: { motivo: null } });
  });

  it('el historial muestra cada acción con usuario y motivo', async () => {
    conPermisos('contabilidad.periodo.leer');
    servidor();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Historial de 01 · Enero' }));
    const panel = await screen.findByRole('region', { name: 'Historial de 01 · Enero' });
    expect(await within(panel).findByText('Motivo: Cierre de enero')).toBeInTheDocument();
    expect(within(panel).getByText('FIX Admin')).toBeInTheDocument();
  });

  it('sin ejercicios explica que no hay periodos abiertos y ofrece crearlo a quien administra', async () => {
    conPermisos('contabilidad.periodo.leer', 'contabilidad.periodo.administrar');
    servidor([]);
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Aún no hay ejercicios/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Crear ejercicio' })).toBeInTheDocument();
  });
});
