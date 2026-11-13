import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { delay, http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
  }: {
    children: React.ReactNode;
    to: string;
    params: { id: string };
  }) => <a href={to.replace('$id', params.id)}>{children}</a>,
}));
import { PolizasDocumento } from './PolizasDocumento';
const API = '*/api/v1/contabilidad/polizas/por-documento';
const documento = {
  modulo: 'facturacion',
  tipo: 'comprobante',
  documentoId: '10000000-0000-0000-0000-000000000001',
} as const;
const fila = (n: number) => ({
  id: `p${n}`,
  folio: `TT-${String(n).padStart(6, '0')}`,
  fecha: '2026-11-12',
  estado: 'Autorizada',
  polizaAnuladaId: null,
  anuladaPorPolizaId: null,
  polizaRelacionadaId: null,
  relacion: null,
});
beforeEach(() => useAuthStore.setState({ permisos: [PermisosCanonicos.ContabilidadPolizasLeer] }));
afterEach(() => useAuthStore.setState({ permisos: [], user: null }));
describe('Pólizas del documento B19', () => {
  it('sin permiso no consulta ni ofrece enlaces', async () => {
    useAuthStore.setState({ permisos: [] });
    const request = vi.fn(() => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 10 }));
    mswServer.use(http.get(API, request));
    render(<PolizasDocumento {...documento} />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText('No tienes permiso para consultar las pólizas contables.'),
    ).toBeInTheDocument();
    expect(request).not.toHaveBeenCalled();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
  it('distingue carga de vacío y no inventa estado pendiente', async () => {
    mswServer.use(
      http.get(API, async () => {
        await delay(50);
        return HttpResponse.json({ items: [], total: 0, offset: 0, limit: 10 });
      }),
    );
    render(<PolizasDocumento {...documento} />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('status')).toHaveTextContent('Consultando pólizas');
    expect(
      await screen.findByText('No hay pólizas relacionadas visibles para este documento.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/pendiente/i)).not.toBeInTheDocument();
  });
  it('403 no es vacío ni ofrece vínculos', async () => {
    mswServer.use(
      http.get(API, () =>
        HttpResponse.json({ status: 403, title: 'Acceso denegado' }, { status: 403 }),
      ),
    );
    render(<PolizasDocumento {...documento} />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText('No tienes acceso a estas pólizas contables.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/No hay pólizas/)).not.toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
  it('un fallo no se presenta como vacío y permite reintentar', async () => {
    let fallo = true;
    mswServer.use(
      http.get(API, () =>
        fallo
          ? HttpResponse.json({ status: 500, title: 'Fallo de prueba' }, { status: 500 })
          : HttpResponse.json({ items: [fila(1)], total: 1, offset: 0, limit: 10 }),
      ),
    );
    render(<PolizasDocumento {...documento} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('No se pudieron consultar las pólizas.')).toBeInTheDocument();
    expect(screen.queryByText(/No hay pólizas/)).not.toBeInTheDocument();
    fallo = false;
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByRole('link', { name: 'TT-000001' })).toHaveAttribute(
      'href',
      '/contabilidad/polizas/p1',
    );
  });
  it('usa identidad canónica y pagina múltiples, con anulación y reversa', async () => {
    const consultas: URLSearchParams[] = [];
    const items = Array.from({ length: 11 }, (_, n) => fila(n + 1));
    const relacionadas = [
      { ...items[0], estado: 'Anulada', anuladaPorPolizaId: 'p2' },
      { ...items[1], polizaAnuladaId: 'p1' },
      { ...items[2], polizaRelacionadaId: 'p1', relacion: 'Reversa' },
      ...items.slice(3),
    ];
    mswServer.use(
      http.get(API, ({ request }) => {
        const q = new URL(request.url).searchParams;
        consultas.push(q);
        const offset = Number(q.get('offset'));
        const limit = Number(q.get('limit'));
        return HttpResponse.json({
          items: relacionadas.slice(offset, offset + limit),
          total: 11,
          offset,
          limit,
        });
      }),
    );
    render(<PolizasDocumento {...documento} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('link', { name: 'TT-000001' })).toHaveAttribute(
      'href',
      '/contabilidad/polizas/p1',
    );
    expect(screen.getByText('Anulada')).toBeInTheDocument();
    expect(screen.getByText('Anulación')).toBeInTheDocument();
    expect(screen.getByText('Reversa')).toBeInTheDocument();
    expect(screen.getAllByRole('link')).toHaveLength(10);
    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await screen.findByRole('link', { name: 'TT-000011' });
    expect(screen.getAllByRole('link')).toHaveLength(1);
    expect(consultas.map((q) => q.get('offset'))).toEqual(['0', '10']);
    expect(consultas[0].get('modulo')).toBe(documento.modulo);
    expect(consultas[0].get('tipo')).toBe(documento.tipo);
    expect(consultas[0].get('documentoId')).toBe(documento.documentoId);
  });
  it('al cambiar de documento no conserva relaciones anteriores', async () => {
    mswServer.use(
      http.get(API, ({ request }) =>
        HttpResponse.json({
          items: [
            fila(
              new URL(request.url).searchParams.get('documentoId') === documento.documentoId
                ? 1
                : 2,
            ),
          ],
          total: 1,
          offset: 0,
          limit: 10,
        }),
      ),
    );
    const view = render(<PolizasDocumento {...documento} />, { wrapper: createQueryWrapper() });
    await screen.findByRole('link', { name: 'TT-000001' });
    view.rerender(
      <PolizasDocumento
        modulo="cuentas_por_pagar"
        tipo="factura_proveedor"
        documentoId="20000000-0000-0000-0000-000000000002"
      />,
    );
    await screen.findByRole('link', { name: 'TT-000002' });
    expect(screen.queryByRole('link', { name: 'TT-000001' })).not.toBeInTheDocument();
  });
});
