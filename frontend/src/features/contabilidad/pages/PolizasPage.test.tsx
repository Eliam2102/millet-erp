import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { resumen } from '../lib/__fixtures__/polizas-fix';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...rest }: { children: React.ReactNode; to: string; params?: { id: string }; [k: string]: unknown }) => {
    const attrs: Record<string, unknown> = { ...rest };
    delete attrs.search;
    return <a href={params ? to.replace('$id', params.id) : to} {...(attrs as object)}>{children}</a>;
  },
}));

import { PolizasPage } from './PolizasPage';

const API = '*/api/v1/contabilidad';

function servidor(peticiones: URLSearchParams[] = []) {
  mswServer.use(
    http.get(`${API}/polizas`, ({ request }) => {
      peticiones.push(new URL(request.url).searchParams);
      return HttpResponse.json({
        items: [
          resumen({ id: 'b1', folio: null, numero: null, estado: 'Borrador', autorizadaPor: null, totalAbonos: 1000 }),
          resumen(),
          resumen({ id: 'p2', folio: 'TT-000124', concepto: 'Anulación de TT-000123', esAnulacion: true, totalCargos: -1234, totalAbonos: -1234 }),
        ],
        total: 3, offset: 0, limit: 10,
      });
    }),
    http.get(`${API}/tipos-documento`, () => HttpResponse.json([])),
    http.get(`${API}/periodos/ejercicios`, () => HttpResponse.json([])),
  );
  return peticiones;
}

afterEach(() => useAuthStore.setState({ permisos: [] }));

describe('<PolizasPage>', () => {
  it('lista folios (o «Borrador»), importes negativos, estados y la marca de anulación', async () => {
    const peticiones = servidor();
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPolizasLeer] });
    render(<PolizasPage search={{ estado: 'Autorizada', q: 'TT-123' }} onSearch={vi.fn()} onAbrir={vi.fn()} />, { wrapper: createQueryWrapper() });

    const borrador = (await screen.findByRole('link', { name: /Ver póliza Borrador/ }, { timeout: 10000 })).closest('tr')!;
    expect(within(borrador).getAllByText('Borrador')).toHaveLength(2); // folio pendiente + badge de estado
    expect(within(borrador).getByText('$1,000.00')).toBeInTheDocument();

    const anulacion = screen.getByRole('link', { name: 'Ver póliza TT-000124' }).closest('tr')!;
    expect(within(anulacion).getAllByText('-$1,234.00')).toHaveLength(2);
    expect(within(anulacion).getByText('Anulación')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver póliza TT-000123' })).toHaveAttribute('href', '/contabilidad/polizas/p1');

    expect(peticiones[0].get('estado')).toBe('Autorizada');
    expect(peticiones[0].get('q')).toBe('TT-123');
    expect(peticiones[0].get('limit')).toBe('10');
    // Sin permiso de captura no se ofrece «Nueva póliza».
    expect(screen.queryByRole('button', { name: 'Nueva póliza' })).not.toBeInTheDocument();
  });

  it('con permiso de captura ofrece «Nueva póliza»', async () => {
    servidor();
    useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasCapturar] });
    render(<PolizasPage search={{}} onSearch={vi.fn()} onAbrir={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('button', { name: 'Nueva póliza' })).toBeInTheDocument();
  });
});
