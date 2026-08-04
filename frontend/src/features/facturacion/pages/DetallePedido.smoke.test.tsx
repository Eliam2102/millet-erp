import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetallePedido } from '@/features/facturacion/pages/DetallePedido';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'p-1' }),
  useSearch: () => ({}),
}));

// FAC-UX-PR2: "Facturar" monta el form de emisión in-place; el smoke lo
// mockea para asertar el prefill sin arrastrar selectores/catálogos.
const prefillSpy = vi.fn();
vi.mock(
  '@/features/facturacion/components/emitir-factura/EmitirFacturaForm',
  () => ({
    EmitirFacturaForm: ({ prefill }: { prefill?: unknown }) => {
      prefillSpy(prefill);
      return <div data-testid="emitir-factura-form" />;
    },
  }),
);

const abrirPedidoSpy = vi.fn();
vi.mock('@/features/facturacion/components/nuevo-pedido-context', () => ({
  useNuevoPedido: () => ({
    abrir: abrirPedidoSpy,
    cerrar: () => {},
    setDirty: () => {},
  }),
}));

function pedido(estado: string) {
  return {
    id: 'p-1',
    numeroPedido: 'MID-001',
    origen: 'Manual',
    estado,
    sucursalId: '33333333-3333-4333-8333-333333333333',
    clienteId: '22222222-2222-4222-8222-222222222222',
    clienteNombre: 'Cliente Demo SA',
    // FAC-ING-PR3: id + nombre del catálogo compartido.canales_venta.
    canalVentaId: 1,
    canalVenta: 'Tienda Cancún',
    comportamientoFiscal: 'MostradorInmediato',
    moneda: 'MXN',
    obraId: null,
    obraNombre: null,
    comentarios: 'Entrega en obra',
    comprobanteVigenteId: null,
    total: 232,
    ranura: null,
    version: 1,
    clienteFiscal: {
      rfc: 'AAA010101AAA',
      regimenFiscal: '601',
      codigoPostalFiscal: '97000',
      usoCfdiDefault: 'G03',
      formaPagoDefault: '03',
      metodoPagoDefault: 'PUE',
      esGenerico: false,
    },
    lineas: [
      {
        posicion: 1,
        productoId: null,
        productoDescripcion: 'Vidrio templado 6mm',
        claveProdServSat: '01010101',
        claveUnidadSat: 'H87',
        cantidad: 2,
        precio: 100,
        descuento: 0,
        requierePedimento: false,
        objetoImp: '02',
        tasaIvaTraslado: 0.16,
        tasaRetencionIva: null,
        tasaRetencionIsr: null,
      },
    ],
  };
}

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  prefillSpy.mockClear();
  abrirPedidoSpy.mockClear();
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

describe('<DetallePedido> — smoke', () => {
  it('renderiza cabecera, líneas y el historial de comprobantes', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json(pedido('Facturado')),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([
          {
            id: 'f-2',
            folio: 'A-2',
            estado: 'Timbrado',
            uuid: '99999999-2222-3333-4444-555555555555',
            total: 232,
            moneda: 'MXN',
            fechaTimbrado: '2026-05-30T11:00:00Z',
            vigente: true,
          },
        ]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(screen.getByText('Vidrio templado 6mm')).toBeInTheDocument();
    expect(screen.getByText(/Historial de comprobantes \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText('A-2')).toBeInTheDocument();
    // "Vigente" aparece como encabezado de columna y como chip de la fila.
    expect(screen.getAllByText('Vigente').length).toBeGreaterThanOrEqual(2);
  });

  it('muestra la ranura del pedido cuando viene informada', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json({ ...pedido('Importado'), ranura: 250.5 }),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(screen.getByText(/Ranura \(descuento del pedido/i)).toBeInTheDocument();
    expect(screen.getByText(/−250\.50 MXN/)).toBeInTheDocument();
  });

  it('"Facturar" visible en Importado + permiso emitir; monta la emisión in-place con prefill', async () => {
    setPermisos(['facturacion.facturas.leer', 'facturacion.facturas.emitir']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json(pedido('Importado')),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    const btn = await screen.findByRole('button', { name: /Facturar/i });
    fireEvent.click(btn);
    expect(await screen.findByTestId('emitir-factura-form')).toBeInTheDocument();
    expect(prefillSpy).toHaveBeenCalledTimes(1);
    const arg = prefillSpy.mock.calls[0][0];
    expect(arg.pedidoFacturableId).toBe('p-1');
    // FAC-ING-PR3: viaja el id del catálogo + el nombre para conservar
    // la opción si el canal fue desactivado.
    expect(arg.canalVenta).toBe(1);
    expect(arg.canalVentaNombre).toBe('Tienda Cancún');
    expect(arg.lineas[0].descripcion).toBe('Vidrio templado 6mm');
    expect(arg.lineas[0].valorUnitario).toBe(100);
    // FAC-UX-PR3: datos fiscales del master viajan en el prefill.
    expect(arg.receptorRfc).toBe('AAA010101AAA');
    expect(arg.receptorRegimenFiscal).toBe('601');
    expect(arg.sucursalId).toBe('33333333-3333-4333-8333-333333333333');
    expect(arg.datosFiscalesIncompletos).toBe(false);
    expect(arg.lineas[0].tasaIvaTraslado).toBe(0.16);
    // El detalle de lectura queda oculto mientras se emite.
    expect(
      screen.queryByText(/Historial de comprobantes/i),
    ).not.toBeInTheDocument();
  });

  it('"Editar" visible en Importado/Manual + permiso capturar; abre el form en edición', async () => {
    setPermisos(['facturacion.facturas.leer', 'facturacion.pedidos.capturar']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json(pedido('Importado')),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    const btn = await screen.findByRole('button', { name: /Editar/i });
    fireEvent.click(btn);
    expect(abrirPedidoSpy).toHaveBeenCalledTimes(1);
    expect(abrirPedidoSpy.mock.calls[0][0]?.id).toBe('p-1');
  });

  it('"Facturar" oculto en estado Facturado', async () => {
    setPermisos(['facturacion.facturas.leer', 'facturacion.facturas.emitir']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json(pedido('Facturado')),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(
      screen.queryByRole('button', { name: /Facturar/i }),
    ).not.toBeInTheDocument();
  });

  it('estado error cuando el pedido no existe', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'No encontrado', status: 404 },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/No se pudo cargar el pedido/i)).toBeInTheDocument(),
    );
  });
});
