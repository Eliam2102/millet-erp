import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PeriodosPage } from './PeriodosPage';

const API = '*/api/v1/contabilidad/periodos';
const MESES = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];

function periodo(numero: number, estado: string) {
  const mes = String(Math.min(numero, 12)).padStart(2, '0');
  return {
    id: `p${numero}`, ejercicioId: 'e1', anio: 2026, numero, nombre: numero === 13 ? 'Ajustes de auditoría' : MESES[numero - 1],
    fechaInicio: numero === 13 ? '2026-12-31' : `2026-${mes}-01`, fechaFin: numero === 13 ? '2026-12-31' : `2026-${mes}-28`,
    estado, esAjuste: numero === 13, abiertoPor: estado === 'NoAbierto' ? null : 'fix.contador', abiertoEn: estado === 'NoAbierto' ? null : '2026-01-02T15:00:00Z',
    cerradoPor: estado === 'Cerrado' ? 'fix.contador' : null, cerradoEn: estado === 'Cerrado' ? '2026-02-05T18:00:00Z' : null,
    reabiertoPor: null, reabiertoEn: null, version: 3,
  };
}

// Enero cerrado, febrero abierto, resto sin abrir.
const EJERCICIO = {
  id: 'e1', anio: 2026, version: 5,
  periodos: Array.from({ length: 13 }, (_, i) => periodo(i + 1, i === 0 ? 'Cerrado' : i === 1 ? 'Abierto' : 'NoAbierto')),
};

function servidor(peticiones: unknown[] = []) {
  mswServer.use(
    http.get(`${API}/ejercicios`, () => HttpResponse.json([EJERCICIO])),
    http.get(`${API}/:id/bitacora`, () => HttpResponse.json([])),
    http.post(`${API}/:id/:accion`, async ({ request, params }) => {
      peticiones.push({ id: params.id, accion: params.accion, body: await request.json(), ifMatch: request.headers.get('If-Match'), idem: !!request.headers.get('Idempotency-Key') });
      return HttpResponse.json(periodo(2, 'Cerrado'));
    }),
  );
}

function conPermisos(...permisos: string[]) {
  useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPeriodoLeer, ...permisos] });
}

async function fila(nombre: string) {
  const celda = await screen.findByText(nombre);
  return within(celda.closest('tr')!);
}

afterEach(() => useAuthStore.setState({ permisos: [] }));

describe('<PeriodosPage>', () => {
  it('abre el historial en un panel, ordena los cambios y vuelve al botón de origen al cerrar', async () => {
    servidor();
    const registros = [
      { id: 'b1', periodoId: 'p1', accion: 'Abrir', estadoAnterior: 'NoAbierto', estadoNuevo: 'Abierto', motivo: null, usuarioNombre: 'Contador inicial', ocurridoEn: '2026-01-02T15:00:00Z', versionResultante: 2 },
      { id: 'b3', periodoId: 'p1', accion: 'Reabrir', estadoAnterior: 'Cerrado', estadoNuevo: 'Abierto', motivo: 'Corrección autorizada\nRevisión de enero', usuarioNombre: 'Contador general', ocurridoEn: '2026-02-06T15:00:00Z', versionResultante: 4 },
      { id: 'b2', periodoId: 'p1', accion: 'Cerrar', estadoAnterior: 'Abierto', estadoNuevo: 'Cerrado', motivo: 'Cierre de enero', usuarioNombre: 'Contador de cierre', ocurridoEn: '2026-02-05T15:00:00Z', versionResultante: 3 },
    ];
    mswServer.use(http.get(`${API}/p1/bitacora`, () => HttpResponse.json(registros)));
    conPermisos();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    const boton = (await fila('1 · Enero')).getByRole('button', { name: 'Bitácora de 1 · Enero' });
    fireEvent.click(boton);
    const dialogo = await screen.findByRole('dialog', { name: 'Bitácora del periodo' });
    expect(within(dialogo).getByText('Enero · Ejercicio 2026')).toBeInTheDocument();
    const lista = await within(dialogo).findByRole('list', { name: 'Cambios del periodo' });
    const cambios = within(lista).getAllByRole('listitem');
    expect(cambios).toHaveLength(3);
    expect(cambios[0]).toHaveTextContent('Reapertura');
    expect(cambios[0]).toHaveTextContent('Contador general');
    expect(cambios[0]).toHaveTextContent('Corrección autorizada Revisión de enero');
    expect(cambios[1]).toHaveTextContent('Cierre de enero');
    expect(cambios[2]).toHaveTextContent('Apertura');
    expect(within(dialogo).queryByRole('button', { name: 'Reabrir' })).not.toBeInTheDocument();
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Volver a periodos' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(boton).toHaveFocus();
  });

  it('permite reintentar la lectura del historial y cerrar el panel con Escape', async () => {
    servidor();
    let lecturas = 0;
    mswServer.use(http.get(`${API}/p2/bitacora`, () => {
      lecturas++;
      return lecturas === 1 ? new HttpResponse(null, { status: 500 }) : HttpResponse.json([]);
    }));
    conPermisos();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    const boton = (await fila('2 · Febrero')).getByRole('button', { name: 'Bitácora de 2 · Febrero' });
    fireEvent.click(boton);
    const dialogo = await screen.findByRole('dialog');
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('No se pudo cargar la bitácora');
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Reintentar' }));
    expect(await within(dialogo).findByText('Sin cambios registrados')).toBeInTheDocument();
    expect(lecturas).toBe(2);
    fireEvent.keyDown(dialogo, { key: 'Escape', code: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(boton).toHaveFocus();
  });

  it('solo lectura: muestra los 13 periodos con su estado y sin acciones', async () => {
    servidor();
    conPermisos();
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('13 · Ajustes de auditoría')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(14);
    expect((await fila('1 · Enero')).getByText('Cerrado')).toBeInTheDocument();
    expect((await fila('2 · Febrero')).getByText('Abierto')).toBeInTheDocument();
    expect((await fila('3 · Marzo')).getByText('No abierto')).toBeInTheDocument();
    for (const nombre of ['Abrir', 'Cerrar', 'Reabrir']) expect(screen.queryByRole('button', { name: nombre })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Nuevo ejercicio/ })).not.toBeInTheDocument();
  });

  it('cada botón aparece solo con su permiso y según el estado del periodo', async () => {
    servidor();
    conPermisos(PermisosCanonicos.ContabilidadPeriodoCerrar);
    const { unmount } = render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    expect((await fila('2 · Febrero')).getByRole('button', { name: 'Cerrar' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Cerrar' })).toHaveLength(1);
    expect(screen.queryByRole('button', { name: 'Reabrir' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Abrir' })).not.toBeInTheDocument();
    unmount();

    conPermisos(PermisosCanonicos.ContabilidadPeriodoReabrir, PermisosCanonicos.ContabilidadPeriodoAdministrar);
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    expect((await fila('1 · Enero')).getByRole('button', { name: 'Reabrir' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cerrar' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Nuevo ejercicio/ })).toBeInTheDocument();
    expect((await fila('3 · Marzo')).getByRole('button', { name: 'Abrir' })).toBeEnabled();
    // El 13 no se abre mientras diciembre no esté cerrado; el botón explica por qué.
    expect((await fila('13 · Ajustes de auditoría')).getByRole('button', { name: 'Abrir' })).toHaveAttribute('title', expect.stringContaining('después de cerrar diciembre'));
  });

  it('cerrar exige motivo de al menos 10 caracteres y envía If-Match e Idempotency-Key', async () => {
    const peticiones: unknown[] = [];
    servidor(peticiones);
    conPermisos(PermisosCanonicos.ContabilidadPeriodoCerrar);
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click((await fila('2 · Febrero')).getByRole('button', { name: 'Cerrar' }));
    const dialogo = await screen.findByRole('dialog');
    const confirmar = within(dialogo).getByRole('button', { name: 'Cerrar periodo' });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialogo).getByLabelText('Motivo'), { target: { value: 'corto' } });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialogo).getByLabelText('Motivo'), { target: { value: 'Cierre mensual de febrero' } });
    expect(confirmar).toBeEnabled();
    fireEvent.click(confirmar);
    await waitFor(() => expect(peticiones).toEqual([
      { id: 'p2', accion: 'cerrar', body: { motivo: 'Cierre mensual de febrero' }, ifMatch: '"3"', idem: true },
    ]));
  });

  it('reabrir avisa que no reabre el inventario y exige motivo', async () => {
    servidor();
    conPermisos(PermisosCanonicos.ContabilidadPeriodoReabrir);
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click((await fila('1 · Enero')).getByRole('button', { name: 'Reabrir' }));
    const dialogo = await screen.findByRole('dialog');
    expect(within(dialogo).getByRole('note')).toHaveTextContent('Reabrir la contabilidad no reabre el inventario');
    expect(within(dialogo).getByRole('button', { name: 'Reabrir periodo' })).toBeDisabled();
  });

  it.each(['crear', 'abrir', 'cerrar'] as const)('%s conserva la clave al reintentar sin respuesta y la cambia al corregir el comando', async (accion) => {
    servidor();
    const claves: string[] = [];
    const ruta = accion === 'crear' ? `${API}/ejercicios` : accion === 'abrir' ? `${API}/ejercicios/e1/abrir` : `${API}/p2/cerrar`;
    mswServer.use(http.post(ruta, ({ request }) => {
      claves.push(request.headers.get('Idempotency-Key')!);
      // Simula una respuesta perdida: el cliente desconoce si se aplicó el cambio.
      return HttpResponse.error();
    }));
    conPermisos(PermisosCanonicos.ContabilidadPeriodoAdministrar, PermisosCanonicos.ContabilidadPeriodoCerrar);
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('2 · Febrero');
    if (accion === 'crear') fireEvent.click(screen.getByRole('button', { name: /Nuevo ejercicio/ }));
    else fireEvent.click((await fila(accion === 'abrir' ? '3 · Marzo' : '2 · Febrero')).getByRole('button', { name: accion === 'abrir' ? 'Abrir' : 'Cerrar' }));
    const dialogo = await screen.findByRole('dialog');
    const campo = within(dialogo).getByLabelText(accion === 'crear' ? 'Año' : /Motivo/);
    if (accion !== 'crear') fireEvent.change(campo, { target: { value: 'Operación contable de prueba' } });
    const confirmar = within(dialogo).getByRole('button', { name: accion === 'crear' ? 'Crear ejercicio' : accion === 'abrir' ? 'Abrir periodo' : 'Cerrar periodo' });
    fireEvent.click(confirmar);
    await within(dialogo).findByRole('alert');
    await waitFor(() => expect(confirmar).toBeEnabled());
    fireEvent.click(confirmar);
    await waitFor(() => expect(claves).toHaveLength(2));
    await within(dialogo).findByRole('alert');
    await waitFor(() => expect(confirmar).toBeEnabled());
    expect(claves[0]).toBeTruthy();
    expect(claves[1]).toBe(claves[0]);
    fireEvent.change(campo, { target: { value: accion === 'crear' ? '2028' : 'Operación contable corregida' } });
    fireEvent.click(confirmar);
    await waitFor(() => expect(claves).toHaveLength(3));
    expect(claves[2]).not.toBe(claves[0]);
  });

  it('rechazo 403 conserva el motivo y muestra el mensaje de permisos', async () => {
    servidor();
    mswServer.use(http.post(`${API}/p1/reabrir`, () => HttpResponse.json(
      { type: 'x', title: 'Prohibido', status: 403, detail: 'No tienes permiso para reabrir este periodo.' }, { status: 403 },
    )));
    conPermisos(PermisosCanonicos.ContabilidadPeriodoReabrir);
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click((await fila('1 · Enero')).getByRole('button', { name: 'Reabrir' }));
    const dialogo = await screen.findByRole('dialog');
    const campo = within(dialogo).getByLabelText('Motivo');
    fireEvent.change(campo, { target: { value: 'Corrección autorizada de enero' } });
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Reabrir periodo' }));
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('No tienes permiso para reabrir este periodo');
    expect(campo).toHaveValue('Corrección autorizada de enero');
  });

  it('409 por versión: pide recargar; 422: muestra el mensaje del servidor', async () => {
    servidor();
    mswServer.use(http.post(`${API}/:id/cerrar`, () => HttpResponse.json(
      { type: 'x', title: 'Conflicto', status: 409, code: 'CONCURRENCY_CONFLICT' }, { status: 409 },
    )));
    conPermisos(PermisosCanonicos.ContabilidadPeriodoCerrar);
    render(<PeriodosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click((await fila('2 · Febrero')).getByRole('button', { name: 'Cerrar' }));
    let dialogo = await screen.findByRole('dialog');
    fireEvent.change(within(dialogo).getByLabelText('Motivo'), { target: { value: 'Cierre mensual de febrero' } });
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cerrar periodo' }));
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('Otro usuario modificó este periodo; recargue');
    expect(within(dialogo).getByRole('button', { name: 'Recargar' })).toBeInTheDocument();

    mswServer.use(http.post(`${API}/:id/cerrar`, () => HttpResponse.json(
      { type: 'x', title: 'Regla', status: 422, code: 'CONTAB_PERIODO_ANTERIOR_ABIERTO', detail: 'Cierre primero el periodo 2026-01 (Enero): los periodos se cierran en orden.' },
      { status: 422 },
    )));
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cerrar periodo' }));
    dialogo = screen.getByRole('dialog');
    await waitFor(() => expect(within(dialogo).getByRole('alert')).toHaveTextContent('los periodos se cierran en orden'));
  });
});
