import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { plantilla, resumenPlantilla } from '../lib/__fixtures__/recurrentes-fix';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...rest }: { children: React.ReactNode; to: string; params?: { id: string }; [k: string]: unknown }) => {
    const attrs: Record<string, unknown> = { ...rest };
    delete attrs.search;
    return <a href={params ? to.replace('$id', params.id) : to} {...(attrs as object)}>{children}</a>;
  },
}));

import { PlantillaRecurrenteDetallePage } from './PlantillaRecurrenteDetallePage';

const API = '*/api/v1/contabilidad';
const problem = (status: number, body: Record<string, unknown>) =>
  HttpResponse.json({ title: 'x', status, ...body }, { status, headers: { 'Content-Type': 'application/problem+json' } });

function servidor(p: ReturnType<typeof plantilla>) {
  mswServer.use(
    http.get(`${API}/polizas-recurrentes/${p.id}`, () => HttpResponse.json(p, { headers: { ETag: `"${p.version}"` } })),
    http.get(`${API}/polizas-recurrentes`, () => HttpResponse.json([resumenPlantilla({ estado: p.estado })])),
    http.get(`${API}/movimientos/sucursales`, () => HttpResponse.json([{ id: 's1', clave: 'MER', nombre: 'FIX Mérida', activa: true }])),
    http.get(`${API}/cuentas/:id`, ({ params }) => HttpResponse.json({ id: params.id, codigo: `FIX-${String(params.id)}`, nombre: 'FIX Cuenta', version: 1 })),
  );
}

function permisos(...extra: string[]) {
  useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadRecurrentesLeer, ...extra] });
}

afterEach(() => useAuthStore.setState({ permisos: [], user: null }));

describe('<PlantillaRecurrenteDetallePage>', () => {
  it('muestra el calendario de aplicaciones con la póliza generada, su reversa y lo que falta por generar', async () => {
    servidor(plantilla());
    permisos();
    render(<PlantillaRecurrenteDetallePage id="r1" search={{}} />, { wrapper: createQueryWrapper() });

    const calendario = await screen.findByRole('region', { name: 'Calendario de aplicaciones' });
    const filas = within(calendario).getAllByRole('row').slice(1);
    expect(filas).toHaveLength(3);
    expect(within(filas[0]).getByText('Septiembre 2026')).toBeInTheDocument();
    expect(within(filas[0]).getByRole('link', { name: 'Ver póliza TT-000130 de Septiembre 2026' })).toHaveAttribute('href', '/contabilidad/polizas/p10');
    expect(within(filas[0]).getByRole('link', { name: 'Ver reversa TT-000131 de Septiembre 2026' })).toHaveAttribute('href', '/contabilidad/polizas/p11');
    expect(within(filas[0]).getByText('Autorizada')).toBeInTheDocument();
    expect(within(filas[1]).getAllByText('Por generar')).toHaveLength(2);
    expect(within(filas[1]).getByText('31 oct 2026')).toBeInTheDocument();

    expect(screen.getByText('Último día del mes')).toBeInTheDocument();
    expect(screen.getByText('1 de 3', { selector: 'dd' })).toBeInTheDocument();
    const partidas = screen.getByRole('region', { name: 'Partidas' });
    expect(await within(partidas).findByText('FIX-c1')).toBeInTheDocument();
    // Sin permiso de administrar no hay acciones.
    expect(screen.queryByRole('button', { name: 'Suspender' })).not.toBeInTheDocument();
  });

  it('quien preparó la plantilla no puede activarla; el botón explica por qué', async () => {
    servidor(plantilla({ estado: 'Borrador', activadaPorId: null, activadaPor: null, activadaEn: null, generadas: 0, aplicaciones: [] }));
    permisos(PermisosCanonicos.ContabilidadRecurrentesAutorizar, PermisosCanonicos.ContabilidadRecurrentesAdministrar);
    useAuthStore.setState({ user: { id: 'u-analista', email: 'fix@example.com', nombre: 'FIX Analista' } });
    render(<PlantillaRecurrenteDetallePage id="r1" search={{}} />, { wrapper: createQueryWrapper() });
    const activar = await screen.findByRole('button', { name: 'Activar' });
    expect(activar).toBeDisabled();
    expect(activar).toHaveAttribute('title', 'No puedes activar una plantilla que tú preparaste: la activa otro usuario.');
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();
  });

  it('activa con If-Match y muestra el rechazo del servidor (R23)', async () => {
    servidor(plantilla({ estado: 'Borrador', activadaPorId: null, activadaPor: null, activadaEn: null, generadas: 0, aplicaciones: [] }));
    let ifMatch: string | null = null;
    mswServer.use(http.post(`${API}/polizas-recurrentes/r1/activar`, ({ request }) => {
      ifMatch = request.headers.get('If-Match');
      return problem(422, { code: 'CONTAB_RECURRENTE_ACTIVA_QUIEN_CREA', detail: 'La plantilla la activa una persona distinta de quien la preparó.' });
    }));
    permisos(PermisosCanonicos.ContabilidadRecurrentesAutorizar);
    useAuthStore.setState({ user: { id: 'u-super', email: 'fix2@example.com', nombre: 'FIX Supervisor' } });
    render(<PlantillaRecurrenteDetallePage id="r1" search={{}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Activar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('La plantilla la activa una persona distinta de quien la preparó.');
    expect(ifMatch).toBe('"2"');
  });

  it('«Generar pendientes» de la plantilla muestra lo generado y lo pendiente con su motivo', async () => {
    servidor(plantilla());
    let url = '';
    mswServer.use(http.post(`${API}/polizas-recurrentes/generar`, ({ request }) => {
      url = request.url;
      return HttpResponse.json({
        generadas: [{ plantillaId: 'r1', plantilla: 'FIX Provisión de renta', anio: 2026, mes: 10, polizaId: 'p12', folio: null, estado: 'Borrador', esReversa: false }],
        pendientes: [{ plantillaId: 'r1', plantilla: 'FIX Provisión de renta', anio: 2026, mes: 11, esReversa: true, codigo: 'CONTAB_PERIODO_NO_ABIERTO', motivo: 'El periodo 2026-11 no está abierto.' }],
      });
    }));
    permisos(PermisosCanonicos.ContabilidadRecurrentesAdministrar);
    render(<PlantillaRecurrenteDetallePage id="r1" search={{}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Generar pendientes' }));
    const panel = await screen.findByRole('region', { name: 'Resultado de la generación' });
    await waitFor(() => expect(url).toContain('plantillaId=r1'));
    expect(within(panel).getByText('1 póliza generada · 1 pendiente.')).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: 'Borrador' })).toHaveAttribute('href', '/contabilidad/polizas/p12');
    expect(within(panel).getByText('Reversa de Noviembre 2026')).toBeInTheDocument();
    expect(within(panel).getByText('El periodo 2026-11 no está abierto.')).toBeInTheDocument();
  });
});
