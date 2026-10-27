import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...rest }: { children: React.ReactNode; to: string; params?: { id: string }; [k: string]: unknown }) => {
    const attrs: Record<string, unknown> = { ...rest };
    delete attrs.search;
    return <a href={params ? to.replace('$id', params.id) : to} {...(attrs as object)}>{children}</a>;
  },
}));

import { MotorContablePage } from './MotorContablePage';

const API = '*/api/v1/contabilidad';

function rechazo(o: Record<string, unknown> = {}) {
  return {
    id: 'x1', tipoEvento: 'cxp.factura.registrada', eventoId: 'e1', causa: 'RolSinCuenta', detalle: 'IVA_ACREDITABLE',
    mensaje: 'FIX El rol IVA_ACREDITABLE no tiene cuenta.', intentos: 2, estado: 'Pendiente',
    creadoEn: '2026-10-20T15:00:00Z', ultimoIntentoEn: '2026-10-21T15:00:00Z', polizaId: null, resueltoPor: null,
    resueltoEn: null, motivoDescarte: null, payload: '{"facturaProveedorId":"f1"}', version: 2, ...o,
  };
}

function rol(o: Record<string, unknown> = {}) {
  return {
    id: 'r1', codigo: 'IVA_ACREDITABLE', descripcion: 'FIX IVA acreditable', cuentaId: null, cuentaCodigo: null,
    cuentaNombre: null, sucursalId: null, moneda: null, vigenteDesde: '2026-01-01', vigenteHasta: null, version: 1, ...o,
  };
}

const peticiones: { url: string; body: unknown; idem: string | null }[] = [];

function servidor() {
  mswServer.use(
    http.get(`${API}/motor/rechazos`, () => HttpResponse.json([
      rechazo(),
      rechazo({ id: 'x2', causa: 'PeriodoCerrado', detalle: null, mensaje: 'FIX Periodo cerrado', intentos: 1 }),
    ])),
    http.get(`${API}/motor/roles`, () => HttpResponse.json([
      rol(),
      rol({ id: 'r2', codigo: 'PROVEEDORES', descripcion: 'FIX Proveedores', cuentaId: 'c2', cuentaCodigo: 'FIX-201', cuentaNombre: 'FIX Proveedores nacionales' }),
    ])),
    http.get(`${API}/sucursales`, () => HttpResponse.json([])),
    http.post(`${API}/motor/rechazos/:id/:accion`, async ({ request, params }) => {
      const texto = await request.text();
      peticiones.push({ url: request.url, body: texto ? JSON.parse(texto) : null, idem: request.headers.get('Idempotency-Key') });
      if (params.accion === 'reprocesar') {
        return HttpResponse.json({
          poliza: { id: 'p9', folio: 'CP-000001', estado: 'Autorizada', idempotente: false },
          rechazoId: null, causa: null, detalle: null, mensaje: null, generada: true,
        });
      }
      return HttpResponse.json(rechazo({ estado: 'Descartado' }));
    }),
  );
}

function conPermisos(...extra: string[]) {
  useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadMotorLeer, ...extra] });
}

beforeEach(() => {
  peticiones.length = 0;
  vi.mocked(toast.success).mockClear();
  vi.mocked(toast.error).mockClear();
});
afterEach(() => useAuthStore.setState({ permisos: [] }));

describe('<MotorContablePage>', () => {
  it('con permiso de rechazos abre en Rechazos y traduce la causa', async () => {
    servidor();
    conPermisos(PermisosCanonicos.ContabilidadRechazosGestionar);
    render(<MotorContablePage search={{}} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Rol sin cuenta')).toBeInTheDocument();
    expect(screen.getByText('Periodo cerrado')).toBeInTheDocument();
    expect(screen.queryByText('RolSinCuenta')).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /^Reprocesar/ })).toHaveLength(2);
    expect(screen.getAllByRole('button', { name: /^Descartar/ })).toHaveLength(2);
  });

  it('sin permiso de gestión no ofrece reprocesar ni descartar', async () => {
    servidor();
    conPermisos();
    render(<MotorContablePage search={{ vista: 'rechazos' }} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Rol sin cuenta')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Reprocesar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Descartar/ })).not.toBeInTheDocument();
  });

  it('sin permiso de rechazos abre en Roles; sin permiso de roles no hay «Cambiar cuenta» ni «Nuevo rol»', async () => {
    servidor();
    conPermisos();
    render(<MotorContablePage search={{}} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('IVA_ACREDITABLE')).toBeInTheDocument();
    expect(screen.getByText('Sin cuenta')).toBeInTheDocument();
    expect(screen.getByText('FIX-201')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambiar cuenta/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nuevo rol' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /^Bitácora de/ })).toHaveLength(2);
  });

  it('con solo contabilidad.motor.leer no consulta las sucursales (evita el 403 de dimensiones); con dimensiones.leer sí', async () => {
    servidor();
    let consultasSucursales = 0;
    mswServer.use(http.get(`${API}/sucursales`, () => {
      consultasSucursales += 1;
      return HttpResponse.json([{ id: 's1', clave: 'MID', nombre: 'Planta México Centro' }]);
    }));
    conPermisos();
    const { unmount } = render(<MotorContablePage search={{ vista: 'roles' }} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('IVA_ACREDITABLE')).toBeInTheDocument();
    expect(consultasSucursales).toBe(0);
    unmount();

    conPermisos(PermisosCanonicos.ContabilidadDimensionesLeer);
    render(<MotorContablePage search={{ vista: 'roles' }} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('IVA_ACREDITABLE')).toBeInTheDocument();
    await waitFor(() => expect(consultasSucursales).toBe(1));
  });

  it('con permiso de roles ofrece «Cambiar cuenta» y «Nuevo rol»', async () => {
    servidor();
    conPermisos(PermisosCanonicos.ContabilidadRolesCuentaAdministrar);
    render(<MotorContablePage search={{ vista: 'roles' }} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByRole('button', { name: 'Cambiar cuenta de IVA_ACREDITABLE' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nuevo rol' })).toBeInTheDocument();
  });

  it('descartar exige motivo de al menos 10 caracteres y lo manda con Idempotency-Key', async () => {
    servidor();
    conPermisos(PermisosCanonicos.ContabilidadRechazosGestionar);
    render(<MotorContablePage search={{}} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });

    await screen.findByText('Rol sin cuenta');
    fireEvent.click(screen.getAllByRole('button', { name: /^Descartar/ })[0]);
    const dialogo = await screen.findByRole('dialog', { name: 'Descartar rechazo' });
    const confirmar = within(dialogo).getByRole('button', { name: 'Descartar rechazo' });
    const motivo = within(dialogo).getByLabelText(/Motivo/);

    fireEvent.change(motivo, { target: { value: 'Corto' } });
    expect(confirmar).toBeDisabled();
    expect(within(dialogo).getByText(/5\/1000/)).toBeInTheDocument();

    fireEvent.change(motivo, { target: { value: 'FIX evento duplicado' } });
    expect(confirmar).toBeEnabled();
    fireEvent.click(confirmar);

    await waitFor(() => expect(peticiones).toHaveLength(1));
    expect(peticiones[0].url).toMatch(/\/rechazos\/x1\/descartar$/);
    expect(peticiones[0].body).toEqual({ motivo: 'FIX evento duplicado' });
    expect(peticiones[0].idem).toBeTruthy();
    await waitFor(() => expect(toast.success).toHaveBeenCalled());
  });

  it('reprocesar avisa el folio de la póliza generada', async () => {
    servidor();
    conPermisos(PermisosCanonicos.ContabilidadRechazosGestionar);
    render(<MotorContablePage search={{}} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });

    await screen.findByText('Rol sin cuenta');
    fireEvent.click(screen.getAllByRole('button', { name: /^Reprocesar/ })[0]);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Póliza CP-000001 generada.'));
    expect(peticiones[0].url).toMatch(/\/rechazos\/x[12]\/reprocesar$/);
    expect(peticiones[0].idem).toBeTruthy();
  });

  it('un rechazo reprocesado enlaza a su póliza', async () => {
    mswServer.use(http.get(`${API}/motor/rechazos`, () =>
      HttpResponse.json([rechazo({ estado: 'Reprocesado', polizaId: 'p9', resueltoPor: 'FIX Contador', resueltoEn: '2026-10-22T15:00:00Z' })])));
    conPermisos(PermisosCanonicos.ContabilidadRechazosGestionar);
    render(<MotorContablePage search={{ estado: 'Reprocesado' }} onSearch={vi.fn()} />, { wrapper: createQueryWrapper() });

    expect(await screen.findByRole('link', { name: 'Ver póliza' })).toHaveAttribute('href', '/contabilidad/polizas/p9');
    expect(screen.queryByRole('button', { name: /^Reprocesar/ })).not.toBeInTheDocument();
  });
});
