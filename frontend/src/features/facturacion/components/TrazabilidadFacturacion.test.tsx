import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
}));

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.facturas.leer'],
    errorMessage: null,
  });
});

afterEach(() => {
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

const arbol = {
  actual: {
    tipo: 7, // FacturaAnticipo
    id: 'fa-1',
    folio: 'FANT-2026-000003',
    estado: 'Timbrado',
    uuid: '11111111-2222-3333-4444-555555555555',
    total: 1160,
    fecha: '2026-07-12T10:00:00Z',
  },
  ascendientes: [],
  descendientes: [
    {
      tipo: 6, // FacturaVenta
      id: 'f-1',
      folio: 'VEN-000003',
      estado: 'Timbrado',
      uuid: '22222222-2222-3333-4444-555555555555',
      total: 11600,
      fecha: '2026-07-12T11:00:00Z',
    },
    {
      tipo: 8, // NotaCredito (sin ruta → chip informativo)
      id: 'nc-1',
      folio: 'NC-000001',
      estado: 'Timbrado',
      uuid: '33333333-2222-3333-4444-555555555555',
      total: 500,
      fecha: '2026-07-12T11:00:00Z',
    },
  ],
};

describe('<TrazabilidadFacturacion> — ANT-PR3 (doc 13 §6.3)', () => {
  it('mapea el árbol y pinta los nodos con labels de Facturación', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/comprobantes/fa-1/arbol-documentos', () =>
        HttpResponse.json(arbol),
      ),
    );
    render(<TrazabilidadFacturacion raiz="comprobante" id="fa-1" />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() =>
      expect(screen.getByText('FANT-2026-000003')).toBeInTheDocument(),
    );
    expect(screen.getByText('Trazabilidad')).toBeInTheDocument();
    expect(screen.getByText('Factura de anticipo')).toBeInTheDocument();
    // Derivados: factura final con link, NC como chip sin link.
    const linkFv = screen.getByRole('link', { name: /VEN-000003/i });
    expect(linkFv).toBeInTheDocument();
    expect(screen.getByText('NC-000001')).toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: /NC-000001/i }),
    ).not.toBeInTheDocument();
  });

  it('sin permiso facturas.leer no renderiza nada', () => {
    useAuthStore.setState({ permisos: ['facturacion.anticipos.leer'] });
    const { container } = render(
      <TrazabilidadFacturacion raiz="comprobante" id="fa-1" />,
      { wrapper: createQueryWrapper() },
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('usa el endpoint de pedidos cuando la raíz es pedido', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/arbol-documentos', () =>
        HttpResponse.json({
          actual: {
            tipo: 5,
            id: 'p-1',
            folio: 'AW-7',
            estado: 'Facturado',
            uuid: null,
            total: null,
            fecha: null,
          },
          ascendientes: [],
          descendientes: [arbol.descendientes[0]],
        }),
      ),
    );
    render(<TrazabilidadFacturacion raiz="pedido" id="p-1" />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(screen.getByText('AW-7')).toBeInTheDocument());
    expect(screen.getByText('Pedido facturable')).toBeInTheDocument();
  });
});
