import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MisIncidenciasPage } from './MisIncidenciasPage';
import { vencida } from '@/features/incidencias/lib/formato';
import { cerrarSesion, fila, sesion } from '@/features/incidencias/test-utils';

let searchActual: Record<string, unknown> = {};

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
  useSearch: () => searchActual,
}));

beforeEach(() => {
  searchActual = {};
  sesion([]);
  mswServer.use(
    http.get('*/api/v1/incidencias/mias/resumen', () => HttpResponse.json({ abiertas: 1, enAtencion: 2 })),
    http.get('*/api/v1/incidencias/pasos', () =>
      HttpResponse.json([{ clave: 'alm.recepcion.diferencia-oc', nombre: 'Diferencia', modulo: 'almacen' }]),
    ),
  );
});

afterEach(cerrarSesion);

describe('<MisIncidenciasPage>', () => {
  it('sin pendientes muestra el estado vacío y los conteos de la campana en las pestañas', async () => {
    mswServer.use(http.get('*/api/v1/incidencias/mias', () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 25 })));
    render(<MisIncidenciasPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('No tienes incidencias pendientes')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Pendientes/ })).toHaveTextContent('3');
    expect(screen.getByRole('tab', { name: /En atención/ })).toHaveTextContent('2');
  });

  it('pide las pendientes sin estado y lista folio, paso, documento y estado', async () => {
    let url = '';
    mswServer.use(
      http.get('*/api/v1/incidencias/mias', ({ request }) => {
        url = request.url;
        return HttpResponse.json({ items: [fila()], total: 1, offset: 0, limit: 25 });
      }),
    );
    render(<MisIncidenciasPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('INC-12')).toBeInTheDocument();
    expect(new URL(url).searchParams.getAll('estado')).toEqual([]);
    expect(screen.getByText('Diferencia de recepción contra la orden de compra')).toBeInTheDocument();
    expect(screen.getByText('REC-000123 · OC-000045')).toBeInTheDocument();
    expect(screen.getByText('Abierta', { selector: '[data-estado]' })).toBeInTheDocument();
    expect(screen.getByText('07/11/2026')).toBeInTheDocument();
  });

  it('la vista «Todas» manda los cuatro estados y el módulo', async () => {
    searchActual = { vista: 'todas', modulo: 'almacen' };
    let url = '';
    mswServer.use(
      http.get('*/api/v1/incidencias/mias', ({ request }) => {
        url = request.url;
        return HttpResponse.json({ items: [], total: 0, offset: 0, limit: 25 });
      }),
    );
    render(<MisIncidenciasPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(url).not.toBe(''));
    const params = new URL(url).searchParams;
    expect(params.getAll('estado')).toEqual(['Abierta', 'EnAtencion', 'Resuelta', 'Cancelada']);
    expect(params.get('modulo')).toBe('almacen');
  });

  it('un error del servidor muestra el aviso con reintentar', async () => {
    mswServer.use(
      http.get('*/api/v1/incidencias/mias', () =>
        HttpResponse.json({ type: 'about:blank', title: 'Error', status: 500 }, { status: 500 }),
      ),
    );
    render(<MisIncidenciasPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('No se pudieron cargar tus incidencias')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });
});

describe('vencida', () => {
  it('solo cuenta compromisos pasados de incidencias pendientes', () => {
    expect(vencida(fila({ fechaCompromiso: '2020-01-01' }))).toBe(true);
    expect(vencida(fila({ fechaCompromiso: '2020-01-01', estado: 'Resuelta' }))).toBe(false);
    expect(vencida(fila({ fechaCompromiso: '2999-01-01' }))).toBe(false);
    expect(vencida(fila({ fechaCompromiso: null }))).toBe(false);
  });
});
