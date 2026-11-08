import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { ReactNode } from 'react';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { PeriodoContable } from '../../api/periodos';
import { ChecklistCierreSheet } from './ChecklistCierreSheet';

// El enlace a la pantalla del módulo dueño no necesita router en esta prueba.
vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children }: { to: string; children: ReactNode }) => <a href={to}>{children}</a>,
}));

const API = '*/api/v1/contabilidad/cierre';

const ENERO: PeriodoContable = {
  id: 'p1', ejercicioId: 'e1', anio: 2026, numero: 1, nombre: 'Enero', fechaInicio: '2026-01-01', fechaFin: '2026-01-31',
  estado: 'Abierto', esAjuste: false, abiertoPor: 'fix', abiertoEn: '2026-01-02T15:00:00Z', cerradoPor: null, cerradoEn: null,
  reabiertoPor: null, reabiertoEn: null, version: 3,
};

function paso(clave: string, nombre: string, estado: string, extra: Record<string, unknown> = {}) {
  return {
    pasoId: clave, clave, nombre, descripcion: '', tipo: 'Automatico', orden: 10, obligatorio: true, estado,
    resumen: estado === 'Pendiente' ? '1 documento(s) PPD pagados o cobrados sin REP.' : 'Cumple', detalle: [],
    ruta: null, bloquea: estado === 'Pendiente', marca: null, ...extra,
  };
}

function checklist(pasos: ReturnType<typeof paso>[], cierres: unknown[] = []) {
  const bloqueantes = pasos.filter((p) => p.bloquea).length;
  return {
    periodoId: 'p1', periodo: '2026-01', nombre: 'Enero', estadoPeriodo: 'Abierto', exigeChecklist: true, puedeCerrar: bloqueantes === 0,
    bloqueantes, excepciones: pasos.filter((p) => p.estado === 'Excepcion').length, evaluadoEn: '2026-02-03T18:00:00Z', pasos, cierres,
  };
}

afterEach(() => useAuthStore.setState({ permisos: [] }));

describe('<ChecklistCierreSheet>', () => {
  it('CA10.9: con REP sin conciliar muestra qué falta, dónde atenderlo y no deja continuar al cierre', async () => {
    mswServer.use(http.get(`${API}/periodos/p1/checklist`, () => HttpResponse.json(checklist([
      paso('CONCILIACION_BANCARIA', 'Conciliación bancaria', 'Cumple'),
      paso('REP_CONCILIADOS', 'REP conciliados', 'Pendiente', {
        detalle: ['Cobro sin REP emitido · FV 1203 · 15/01/2026'], ruta: '/contabilidad/reportes/liquidacion-iva',
      }),
    ]))));
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPeriodoCerrar] });
    const onCerrar = vi.fn();
    render(<ChecklistCierreSheet periodo={ENERO} onClose={() => {}} onCerrarPeriodo={onCerrar} />, { wrapper: createQueryWrapper() });

    const panel = await screen.findByRole('dialog', { name: 'Checklist de cierre' });
    expect(await within(panel).findByText('1 paso obligatorio pendiente')).toBeInTheDocument();
    const rep = within(panel).getByText('REP conciliados').closest('li')!;
    expect(within(rep).getByText('Pendiente')).toBeInTheDocument();
    expect(within(rep).getByText('Impide cerrar el periodo.')).toBeInTheDocument();
    expect(within(rep).getByText(/FV 1203/)).toBeInTheDocument();
    expect(within(rep).getByRole('link', { name: /Ir a la pantalla/ })).toHaveAttribute('href', '/contabilidad/reportes/liquidacion-iva');
    // Sin el permiso de excepción no se ofrece.
    expect(within(rep).queryByRole('button', { name: 'Autorizar excepción' })).not.toBeInTheDocument();
    const cerrar = within(panel).getByRole('button', { name: 'Cerrar periodo' });
    expect(cerrar).toBeDisabled();
    expect(cerrar).toHaveAttribute('title', expect.stringContaining('excepción autorizada'));
  });

  it('CON-12-a: autorizar la excepción pide motivo y la envía con Idempotency-Key', async () => {
    const peticiones: unknown[] = [];
    mswServer.use(
      http.get(`${API}/periodos/p1/checklist`, () => HttpResponse.json(checklist([paso('REP_CONCILIADOS', 'REP conciliados', 'Pendiente')]))),
      http.post(`${API}/periodos/:id/pasos/:clave/excepcion`, async ({ request, params }) => {
        peticiones.push({ id: params.id, clave: params.clave, body: await request.json(), idem: !!request.headers.get('Idempotency-Key') });
        return HttpResponse.json({ id: 'm1', tipo: 'Excepcion', motivo: 'x', usuarioNombre: 'Laura', registradaEn: '2026-02-03T18:00:00Z' });
      }),
    );
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPeriodoCerrar, PermisosCanonicos.ContabilidadCierreAutorizarExcepcion] });
    render(<ChecklistCierreSheet periodo={ENERO} onClose={() => {}} onCerrarPeriodo={() => {}} />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar excepción' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Autorizar excepción' });
    const confirmar = within(dialogo).getByRole('button', { name: 'Autorizar excepción' });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialogo).getByLabelText(/Motivo/), { target: { value: 'El proveedor manda el REP el lunes' } });
    fireEvent.click(confirmar);
    await waitFor(() => expect(peticiones).toEqual([
      { id: 'p1', clave: 'REP_CONCILIADOS', body: { motivo: 'El proveedor manda el REP el lunes' }, idem: true },
    ]));
  });

  it('una excepción vigente muestra quién la autorizó y deja continuar al cierre', async () => {
    mswServer.use(http.get(`${API}/periodos/p1/checklist`, () => HttpResponse.json(checklist([
      paso('REP_CONCILIADOS', 'REP conciliados', 'Excepcion', {
        bloquea: false,
        marca: { id: 'm1', tipo: 'Excepcion', motivo: 'El proveedor manda el REP el lunes', usuarioNombre: 'Laura Contadora General', registradaEn: '2026-02-03T18:00:00Z' },
      }),
    ]))));
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPeriodoCerrar] });
    const onCerrar = vi.fn();
    render(<ChecklistCierreSheet periodo={ENERO} onClose={() => {}} onCerrarPeriodo={onCerrar} />, { wrapper: createQueryWrapper() });

    const panel = await screen.findByRole('dialog', { name: 'Checklist de cierre' });
    expect(await within(panel).findByText('Excepción autorizada')).toBeInTheDocument();
    expect(within(panel).getByText('Laura Contadora General')).toBeInTheDocument();
    expect(within(panel).getByText('El proveedor manda el REP el lunes')).toBeInTheDocument();
    const cerrar = within(panel).getByRole('button', { name: 'Cerrar periodo' });
    expect(cerrar).toBeEnabled();
    fireEvent.click(cerrar);
    expect(onCerrar).toHaveBeenCalledWith(ENERO);
  });

  it('O3: con los resultados del año en cero, «Generar cierre anual» queda deshabilitado y dice por qué', async () => {
    const DIC_AJUSTE: PeriodoContable = { ...ENERO, numero: 13, nombre: 'Ajustes', esAjuste: true };
    mswServer.use(http.get(`${API}/periodos/p1/checklist`, () => HttpResponse.json(checklist([
      paso('CIERRE_ANUAL', 'Cierre anual: resultados en cero', 'Cumple', { resumen: 'Resultados de 2026 en cero.' }),
    ]))));
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPeriodoCerrar, PermisosCanonicos.ContabilidadCierreAnual] });
    render(<ChecklistCierreSheet periodo={DIC_AJUSTE} onClose={() => {}} onCerrarPeriodo={() => {}} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByRole('button', { name: 'Generar cierre anual' })).toBeDisabled();
    expect(screen.getByText(/ya están en cero: no hay cierre anual que generar/)).toBeInTheDocument();
  });

  it('con resultados por cerrar, «Generar cierre anual» está disponible', async () => {
    const DIC_AJUSTE: PeriodoContable = { ...ENERO, numero: 13, nombre: 'Ajustes', esAjuste: true };
    mswServer.use(http.get(`${API}/periodos/p1/checklist`, () => HttpResponse.json(checklist([
      paso('CIERRE_ANUAL', 'Cierre anual: resultados en cero', 'Pendiente'),
    ]))));
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPeriodoCerrar, PermisosCanonicos.ContabilidadCierreAnual] });
    render(<ChecklistCierreSheet periodo={DIC_AJUSTE} onClose={() => {}} onCerrarPeriodo={() => {}} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByRole('button', { name: 'Generar cierre anual' })).toBeEnabled();
  });

  it('un paso manual se confirma con nota (no con excepción)', async () => {
    mswServer.use(http.get(`${API}/periodos/p1/checklist`, () => HttpResponse.json(checklist([
      paso('NOMINA_VS_CFDI', 'Nóminas contra CFDI emitidos', 'Pendiente', { tipo: 'Manual', resumen: 'Paso manual: confírmelo con una nota cuando esté hecho.' }),
    ]))));
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPeriodoCerrar] });
    render(<ChecklistCierreSheet periodo={ENERO} onClose={() => {}} onCerrarPeriodo={() => {}} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByRole('button', { name: 'Confirmar paso' })).toBeInTheDocument();
    expect(screen.getByText(/Se confirma a mano/)).toBeInTheDocument();
  });
});
