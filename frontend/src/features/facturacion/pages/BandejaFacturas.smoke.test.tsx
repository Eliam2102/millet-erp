import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaFacturas } from '@/features/facturacion/pages/BandejaFacturas';
import { useAuthStore } from '@/lib/auth/auth-store';

// F6.1: search params mutables para probar el filtro «Clase».
const routerMocks = vi.hoisted(() => ({
  search: {} as Record<string, unknown>,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => routerMocks.search,
}));

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.facturas.leer', 'facturacion.facturas.emitir'],
    errorMessage: null,
  });
});

afterEach(() => {
  routerMocks.search = {};
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<BandejaFacturas> — smoke', () => {
  it('estado empty + link Nueva factura visible con permiso', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json({ items: [], sinAsignarCount: null }),
      ),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText(/Sin facturas/i)).toBeInTheDocument());
    // FAC-UX-PR2: navega a /facturacion/facturas/nueva (ya no abre Sheet).
    const link = screen.getByRole('link', { name: /Nueva factura/i });
    expect(link).toHaveAttribute('href', '/facturacion/facturas/nueva');
  });

  it('renderiza una factura timbrada con folio y receptor', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json({
          items: [
            {
              id: 'f-1',
              folio: 'A-1',
              estado: 'Timbrado',
              uuid: '11111111-2222-3333-4444-555555555555',
              receptorNombre: 'Público en general',
              receptorRfc: 'XAXX010101000',
              total: 116,
              moneda: 'MXN',
              fechaTimbrado: '2026-05-30T10:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Público en general')).toBeInTheDocument());
    expect(screen.getByText('A-1')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Timbrada' })).toBeInTheDocument();
  });

  it('F6.1: badges de clase y filtro «Clase» sincronizado con ?clase=', async () => {
    routerMocks.search = { clase: 'Emergencia' };
    let urlRecibida = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', ({ request }) => {
        urlRecibida = request.url;
        return HttpResponse.json({
          items: [
            {
              id: 'f-2',
              folio: 'VD-1',
              estado: 'Timbrado',
              uuid: null,
              receptorNombre: 'Cliente Emergencia',
              receptorRfc: 'AAA010101AAA',
              total: 116,
              moneda: 'MXN',
              fechaTimbrado: null,
              claseManual: 'Emergencia',
              subtipoVentaDirecta: null,
              esEmergencia: true,
            },
            {
              id: 'f-3',
              folio: 'VD-2',
              estado: 'Timbrado',
              uuid: null,
              receptorNombre: 'Cliente Activo',
              receptorRfc: 'BBB010101BBB',
              total: 50,
              moneda: 'MXN',
              fechaTimbrado: null,
              claseManual: 'VentaDirecta',
              subtipoVentaDirecta: 'Activo',
              esEmergencia: false,
            },
          ],
          sinAsignarCount: null,
        });
      }),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Emergencia')).toBeInTheDocument(),
    );
    expect(new URL(urlRecibida).searchParams.get('clase')).toBe('Emergencia');
    const badges = screen.getAllByTestId('clase-pedido-badge');
    expect(badges.map((b) => b.textContent)).toEqual([
      'Emergencia',
      'Venta directa · Activo',
    ]);
    expect(screen.getByRole('combobox', { name: /Filtrar por clase/i })).toHaveTextContent(
      'Emergencia',
    );
  });

  it('estado error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/No se pudieron cargar las facturas/i)).toBeInTheDocument(),
    );
  });

  it('P19: con permiso de pólizas, columna Póliza con enlace y una sola llamada por página', async () => {
    useAuthStore.setState({
      permisos: ['facturacion.facturas.leer', 'contabilidad.polizas.leer'],
    });
    const factura = (id: string, folio: string) => ({
      id,
      folio,
      estado: 'Timbrado',
      uuid: null,
      receptorNombre: 'Cliente',
      receptorRfc: 'AAA010101AAA',
      total: 116,
      moneda: 'MXN',
      fechaTimbrado: '2026-05-30T10:00:00Z',
    });
    const llamadas: string[] = [];
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json({ items: [factura('f-1', 'A-1'), factura('f-2', 'A-2')], sinAsignarCount: null }),
      ),
      http.get('*/api/v1/contabilidad/polizas/por-documentos', ({ request }) => {
        llamadas.push(request.url);
        return HttpResponse.json([
          {
            documentoId: 'f-1',
            polizas: [{ id: 'p-1', folio: 'IG-0001', fecha: '2026-05-30', estado: 'Autorizada' }],
          },
        ]);
      }),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('link', { name: 'IG-0001' })).toHaveAttribute(
      'href',
      '/contabilidad/polizas/$id',
    );
    expect(screen.getByRole('columnheader', { name: 'Póliza' })).toBeInTheDocument();
    expect(llamadas).toHaveLength(1);
    const ids = new URL(llamadas[0] ?? '').searchParams.getAll('documentoIds');
    expect(ids).toEqual(['f-1', 'f-2']);
  });

  it('P19: sin permiso de pólizas la bandeja funciona sin la columna', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json({
          items: [
            {
              id: 'f-1',
              folio: 'A-1',
              estado: 'Timbrado',
              uuid: null,
              receptorNombre: 'Cliente',
              receptorRfc: 'AAA010101AAA',
              total: 116,
              moneda: 'MXN',
              fechaTimbrado: null,
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('A-1')).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Póliza' })).not.toBeInTheDocument();
  });
});
