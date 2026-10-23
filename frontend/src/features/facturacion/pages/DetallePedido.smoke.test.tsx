import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
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
vi.mock('@/features/facturacion/components/emitir-factura/EmitirFacturaForm', () => ({
  EmitirFacturaForm: ({ prefill }: { prefill?: unknown }) => {
    prefillSpy(prefill);
    return <div data-testid="emitir-factura-form" />;
  },
}));

// U1.8a: el PDF de la proforma se genera con @react-pdf; el smoke solo
// verifica que se muestre en el visor o se descargue con la respuesta del API.
const descargarProformaSpy = vi.hoisted(() => vi.fn());
const generarBlobSpy = vi.hoisted(() => vi.fn());
vi.mock('@/components/erp/reportes/exporters/proforma-cfdi-exporter', () => ({
  descargarProformaCfdi: descargarProformaSpy,
  generarProformaPdfBlob: generarBlobSpy,
}));

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
    // F6.1: pedido manual clasificado como venta directa.
    claseManual: 'VentaDirecta' as string | null,
    subtipoVentaDirecta: 'Almacenable' as string | null,
    esEmergencia: false,
    motivoEmergencia: null as string | null,
    referenciaPedidoAw: null as string | null,
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
  descargarProformaSpy.mockReset();
  generarBlobSpy.mockReset();
  generarBlobSpy.mockResolvedValue(new Blob(['%PDF'], { type: 'application/pdf' }));
  URL.createObjectURL = vi.fn(() => 'blob:proforma');
  URL.revokeObjectURL = vi.fn();
  // U1.8a: por omisión el pedido aún no tiene proforma.
  mswServer.use(
    http.get(
      '*/api/v1/facturacion/pedidos-facturables/p-1/proformas/ultima',
      () => new HttpResponse(null, { status: 204 }),
    ),
  );
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
    await waitFor(() => expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument());
    expect(screen.getByText('Vidrio templado 6mm')).toBeInTheDocument();
    expect(screen.getByText(/Historial de comprobantes \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText('A-2')).toBeInTheDocument();
    // "Vigente" aparece como encabezado de columna y como chip de la fila.
    expect(screen.getAllByText('Vigente').length).toBeGreaterThanOrEqual(2);
  });

  it('86e3tfdw2: pedido a crédito muestra badge «Crédito» y la condición de pago', async () => {
    setPermisos(['facturacion.facturas.leer', 'facturacion.pedidos.leer-credito']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json({
          ...pedido('Importado'),
          origen: 'Aw',
          claseManual: null,
          subtipoVentaDirecta: null,
          esCredito: true,
          metodoPagoOrigen: 'PPD',
          condicionPagoOrigen: 'CRÉDITO 30 DÍAS',
        }),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(screen.getByText('Crédito', { selector: '[data-slot="badge"]' })).toBeInTheDocument();
    expect(screen.getByText('Condición de pago')).toBeInTheDocument();
    expect(screen.getByText('CRÉDITO 30 DÍAS')).toBeInTheDocument();
  });

  it('86e3tfdw2: pedido de contado no muestra badge ni condición de pago', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json({ ...pedido('Importado'), esCredito: false }),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(screen.queryByText('Crédito', { selector: '[data-slot="badge"]' })).not.toBeInTheDocument();
    expect(screen.queryByText('Condición de pago')).not.toBeInTheDocument();
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
    await waitFor(() => expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument());
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
    // F6.1: la clase viaja al form de emisión (serie de Venta directa).
    expect(arg.claseManual).toBe('VentaDirecta');
    expect(arg.subtipoVentaDirecta).toBe('Almacenable');
    expect(arg.origen).toBe('Manual');
    // El detalle de lectura queda oculto mientras se emite.
    expect(screen.queryByText(/Historial de comprobantes/i)).not.toBeInTheDocument();
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
    await waitFor(() => expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Facturar/i })).not.toBeInTheDocument();
  });

  it('F6.1: emergencia muestra badge, motivo y referencia A+W', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json({
          ...pedido('Importado'),
          claseManual: 'Emergencia',
          subtipoVentaDirecta: null,
          esEmergencia: true,
          motivoEmergencia: 'A+W sin servicio por mantenimiento',
          referenciaPedidoAw: 'AW-7788',
        }),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Emergencia')).toBeInTheDocument();
    expect(screen.getByText('A+W sin servicio por mantenimiento')).toBeInTheDocument();
    expect(screen.getByText('AW-7788')).toBeInTheDocument();
  });

  it('F6.1: venta directa muestra «Venta directa · Subtipo»', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json(pedido('Importado')),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Venta directa · Almacenable')).toBeInTheDocument();
    expect(screen.queryByText(/Captura de emergencia/i)).not.toBeInTheDocument();
  });

  it('F6.1: pedido manual sin clase deshabilita «Facturar» y avisa', async () => {
    setPermisos(['facturacion.facturas.leer', 'facturacion.facturas.emitir']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json({
          ...pedido('Importado'),
          claseManual: null,
          subtipoVentaDirecta: null,
        }),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    const btn = await screen.findByRole('button', { name: /Facturar/i });
    expect(btn).toBeDisabled();
    expect(screen.getByText('Pedido manual sin clase')).toBeInTheDocument();
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

it.each([false, true])(
  'muestra Pedimento sólo si alguna partida lo requiere (%s)',
  async (requierePedimento) => {
    setPermisos(['facturacion.facturas.leer']);
    const data = pedido('Facturado');
    data.lineas[0].requierePedimento = requierePedimento;
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () => HttpResponse.json(data)),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await screen.findByText('Vidrio templado 6mm');
    expect(screen.queryByRole('columnheader', { name: 'Pedimento' }) != null).toBe(
      requierePedimento,
    );
    expect(screen.getByRole('columnheader', { name: 'Clave SAT' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Descuento' })).toBeInTheDocument();
  },
);

describe('<DetallePedido> — proforma (U1.8a)', () => {
  function proforma(version: number) {
    return {
      id: `pf-${version}`,
      pedidoFacturableId: 'p-1',
      folio: `PF-MID-001-${version}`,
      version,
      generadaEn: '2026-10-21T15:30:00Z',
      generadaPor: 'Ana López',
      numeroPedido: 'MID-001',
      avisos: [],
      contenido: {
        emisor: {
          rfc: 'MIL010101ABC',
          nombre: 'Millet',
          regimenFiscal: '601',
          lugarExpedicion: '76120',
        },
        receptor: {
          rfc: 'AAA010101AAA',
          nombre: 'Cliente Demo SA',
          regimenFiscal: '601',
          codigoPostal: '97000',
          usoCfdi: 'G03',
        },
        metodoPago: 'PUE',
        formaPago: '03',
        moneda: 'MXN',
        tipoCambio: null,
        exportacion: '01',
        serieFiscalPrevista: 'Cfdi',
        conceptos: [],
        totales: {
          subtotal: 200,
          descuento: 0,
          impuestosTrasladados: 32,
          retenciones: 0,
          total: 232,
        },
      },
    };
  }
  function pedidoBase(estado: string) {
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json(pedido(estado)),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
  }

  it('«Generar proforma» con permiso emitir en Importado: POST con Idempotency-Key, la muestra en el visor y en la tarjeta', async () => {
    setPermisos(['facturacion.facturas.leer', 'facturacion.facturas.emitir']);
    pedidoBase('Importado');
    let idempotencyKey: string | null = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/pedidos-facturables/p-1/proformas', ({ request }) => {
        idempotencyKey = request.headers.get('Idempotency-Key');
        return HttpResponse.json(proforma(1), { status: 201 });
      }),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/aún no tiene proforma/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Generar proforma/i }));
    const visor = await screen.findByRole('dialog', { name: /Proforma PF-MID-001-1/ });
    expect(
      await within(visor).findByTitle('Vista previa de la proforma PF-MID-001-1'),
    ).toHaveAttribute('src', 'blob:proforma');
    expect(generarBlobSpy.mock.calls[0][0].folio).toBe('PF-MID-001-1');
    expect(descargarProformaSpy).not.toHaveBeenCalled();
    expect(idempotencyKey).toBeTruthy();
    // «Descargar» sigue disponible desde el visor.
    fireEvent.click(within(visor).getByRole('button', { name: /Descargar/ }));
    await waitFor(() => expect(descargarProformaSpy).toHaveBeenCalledTimes(1));
    expect(descargarProformaSpy.mock.calls[0][0].folio).toBe('PF-MID-001-1');
  });

  it('«Ver proforma» del encabezado y de la tarjeta abre la última en el visor sin descargar', async () => {
    setPermisos(['facturacion.facturas.leer']);
    pedidoBase('Facturado');
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/proformas/ultima', () =>
        HttpResponse.json(proforma(2)),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Ver proforma' }));
    const visor = await screen.findByRole('dialog', { name: /Proforma PF-MID-001-2/ });
    expect(
      await within(visor).findByTitle('Vista previa de la proforma PF-MID-001-2'),
    ).toBeInTheDocument();
    expect(within(visor).getByText(/Sin valor fiscal/)).toBeInTheDocument();
    fireEvent.click(within(visor).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:proforma');

    fireEvent.click(screen.getByRole('button', { name: 'Ver proforma PF-MID-001-2' }));
    expect(
      await screen.findByRole('dialog', { name: /Proforma PF-MID-001-2/ }),
    ).toBeInTheDocument();
    expect(descargarProformaSpy).not.toHaveBeenCalled();
  });

  it('muestra la última proforma con «Descargar» para quien solo puede leer', async () => {
    setPermisos(['facturacion.facturas.leer']);
    pedidoBase('Importado');
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/proformas/ultima', () =>
        HttpResponse.json(proforma(2)),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('PF-MID-001-2')).toBeInTheDocument();
    expect(screen.getByText(/Ana López/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Generar proforma/i })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Descargar proforma/i }));
    await waitFor(() => expect(descargarProformaSpy).toHaveBeenCalledTimes(1));
    expect(descargarProformaSpy.mock.calls[0][0].version).toBe(2);
  });

  it('«Generar proforma» oculto si el pedido ya no está Importado', async () => {
    setPermisos(['facturacion.facturas.leer', 'facturacion.facturas.emitir']);
    pedidoBase('Facturado');
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    await screen.findByText('Vidrio templado 6mm');
    expect(screen.queryByRole('button', { name: /Generar proforma/i })).not.toBeInTheDocument();
  });
});
