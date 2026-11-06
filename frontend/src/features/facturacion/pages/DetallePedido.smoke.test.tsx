import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetallePedido } from '@/features/facturacion/pages/DetallePedido';
import { useAuthStore } from '@/lib/auth/auth-store';

const navigateSpy = vi.hoisted(() => vi.fn());
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => navigateSpy,
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
  EmitirFacturaForm: ({
    prefill,
    onSuccess,
  }: {
    prefill?: unknown;
    onSuccess: (res: { id: string; folio: string; uuid: string | null }) => void;
  }) => {
    prefillSpy(prefill);
    return (
      <div data-testid="emitir-factura-form">
        <button type="button" onClick={() => onSuccess({ id: 'f-9', folio: 'FV 9', uuid: 'u-9' })}>
          Simular emisión
        </button>
      </div>
    );
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
    canalVenta: 'Sucursal Cancún',
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

/** FAC-08: respuesta de GET /salidas sobre la partida 1 del pedido (2 piezas). */
function salidasDe(
  salidas: Array<{
    id: string;
    numeroParcialidad: number;
    cantidad: number;
    importe: number;
    facturaId?: string | null;
    folio?: string | null;
    estadoFactura?: string | null;
    origen?: number;
    referencia?: string;
    descartadaEn?: string | null;
    motivoDescarte?: string | null;
  }>,
  enSalidas: number,
  facturada: number,
) {
  return {
    pedidoFacturableId: 'p-1',
    totalPedido: 200,
    totalFacturado: salidas.filter((s) => s.facturaId).reduce((a, s) => a + s.importe, 0),
    avance: [
      { posicion: 1, descripcion: 'Vidrio templado 6mm', pedida: 2, enSalidas, facturada },
    ],
    salidas: salidas.map((s) => ({
      id: s.id,
      numeroParcialidad: s.numeroParcialidad,
      origen: s.origen ?? 3,
      referencia: s.referencia ?? `SAL-${s.numeroParcialidad}`,
      registradaEn: '2026-11-02T16:00:00Z',
      total: s.importe,
      facturaId: s.facturaId ?? null,
      folio: s.folio ?? null,
      uuid: null,
      estadoFactura: s.estadoFactura ?? null,
      descartadaEn: s.descartadaEn ?? null,
      motivoDescarte: s.motivoDescarte ?? null,
      lineas: [{ posicion: 1, cantidad: s.cantidad, importe: s.importe }],
    })),
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
    // FAC-08: por omisión el pedido no tiene salidas parciales.
    http.get('*/api/v1/facturacion/pedidos-facturables/p-1/salidas', () =>
      HttpResponse.json(salidasDe([], 1, 0)),
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

  it('86e3wzcn4: un pedido existente con «Traslado (Carta Porte)» se consulta igual', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json({ ...pedido('Facturado'), comportamientoFiscal: 'TrasladoConCartaPorte' }),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
        HttpResponse.json([]),
      ),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Traslado (Carta Porte)')).toBeInTheDocument();
    expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument();
  });

  it('86e3tfd6n: muestra los anticipos cobrados antes de que llegara el pedido', async () => {
    setPermisos(['facturacion.facturas.leer']);
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
        HttpResponse.json({
          ...pedido('Importado'),
          anticiposLigados: [
            { anticipoId: 'a-1', facturaAnticipoId: 'fa-1', folio: 'FANT-7', montoCobrado: 1160, saldo: 1160, estado: 'Abierto' },
          ],
        }),
      ),
      http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () => HttpResponse.json([])),
    );
    render(<DetallePedido />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Anticipos del pedido \(1\)/)).toBeInTheDocument();
    expect(screen.getByText('FANT-7')).toBeInTheDocument();
    expect(screen.getAllByText('$1,160.00')).toHaveLength(2); // cobrado y saldo
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
    expect(screen.getByText(/−\$250\.50/)).toBeInTheDocument();
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
    expect(arg.canalVentaNombre).toBe('Sucursal Cancún');
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

  describe('86e3tfdwu: cobrar en caja tras emitir', () => {
    function prepararEmision(estadoCaja: string) {
      mswServer.use(
        http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
          HttpResponse.json(pedido('Importado')),
        ),
        http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
          HttpResponse.json([]),
        ),
        http.get('*/api/v1/facturacion/cajas/sesion-actual', () =>
          HttpResponse.json({
            sesion: { id: 's-1', estado: estadoCaja, sucursalId: 'x', cajaNombre: 'Caja 1' },
            diaAnteriorPendiente: false,
          }),
        ),
        http.get('*/api/v1/facturacion/cobros/cobrables', () =>
          HttpResponse.json([{ comprobanteId: 'f-9', total: 116 }]),
        ),
      );
    }

    async function emitir() {
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      fireEvent.click(await screen.findByRole('button', { name: /Facturar/i }));
      fireEvent.click(await screen.findByRole('button', { name: 'Simular emisión' }));
      await screen.findByText(/Historial de comprobantes/i);
    }

    it('con caja abierta muestra «Cobrar en caja» de la factura emitida y navega por router', async () => {
      navigateSpy.mockClear();
      setPermisos([
        'facturacion.facturas.leer',
        'facturacion.facturas.emitir',
        'facturacion.caja.operar',
      ]);
      prepararEmision('Abierta');
      await emitir();
      const aviso = await screen.findByRole('status');
      expect(within(aviso).getByText('FV 9')).toBeInTheDocument();
      fireEvent.click(within(aviso).getByRole('button', { name: 'Cobrar en caja' }));
      expect(navigateSpy).toHaveBeenCalledWith({ href: '/facturacion/caja?cobrar=f-9&folio=FV+9' });
    });

    it('sin caja abierta no ofrece el cobro', async () => {
      setPermisos([
        'facturacion.facturas.leer',
        'facturacion.facturas.emitir',
        'facturacion.caja.operar',
      ]);
      prepararEmision('EnArqueo');
      await emitir();
      await waitFor(() =>
        expect(screen.queryByRole('button', { name: 'Cobrar en caja' })).not.toBeInTheDocument(),
      );
    });
  });

  describe('FAC-08: salidas parciales', () => {
    const permisosCompletos = [
      'facturacion.facturas.leer',
      'facturacion.facturas.emitir',
      'facturacion.pedidos.capturar',
    ];

    function prepararPedido(salidas: Parameters<typeof salidasDe>[0], enSalidas: number, facturada: number) {
      mswServer.use(
        http.get('*/api/v1/facturacion/pedidos-facturables/p-1', () =>
          HttpResponse.json(pedido('Importado')),
        ),
        http.get('*/api/v1/facturacion/pedidos-facturables/p-1/comprobantes', () =>
          HttpResponse.json([]),
        ),
        http.get('*/api/v1/facturacion/pedidos-facturables/p-1/salidas', () =>
          HttpResponse.json(salidasDe(salidas, enSalidas, facturada)),
        ),
      );
    }

    it('sin salidas ofrece registrar una y deja facturar el pedido completo', async () => {
      setPermisos(permisosCompletos);
      prepararPedido([], 0, 0);
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      expect(await screen.findByText(/Salidas parciales \(0\)/)).toBeInTheDocument();
      expect(screen.getByText(/El pedido no tiene salidas parciales/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Registrar salida/ })).toBeEnabled();
      expect(screen.getByRole('button', { name: /^Facturar$/ })).toBeEnabled();
    });

    it('con salidas muestra avance y parcialidades, y el pedido ya no se factura completo', async () => {
      setPermisos(permisosCompletos);
      prepararPedido(
        [
          { id: 's-1', numeroParcialidad: 1, cantidad: 1, importe: 100, facturaId: 'f-1', folio: 'FV 10', estadoFactura: 'Timbrado' },
          { id: 's-2', numeroParcialidad: 2, cantidad: 1, importe: 100 },
        ],
        2,
        1,
      );
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      expect(await screen.findByText(/Salidas parciales \(2\)/)).toBeInTheDocument();
      const parcialidad1 = screen.getByTestId('salida-1');
      expect(within(parcialidad1).getByText('FV 10')).toBeInTheDocument();
      expect(within(parcialidad1).getByText('Timbrada')).toBeInTheDocument();
      const parcialidad2 = screen.getByTestId('salida-2');
      expect(within(parcialidad2).getByText('Por facturar')).toBeInTheDocument();
      expect(within(parcialidad2).getByText('Sin factura')).toBeInTheDocument();
      // La parcialidad facturada no ofrece volver a facturarse.
      expect(
        screen.queryByRole('button', { name: 'Facturar la parcialidad 1' }),
      ).not.toBeInTheDocument();
      const completo = screen.getByRole('button', { name: /^Facturar$/ });
      expect(completo).toBeDisabled();
      expect(completo).toHaveAttribute('title', expect.stringMatching(/salidas parciales/));
      // Todas las piezas ya salieron: no hay más que registrar.
      expect(screen.getByRole('button', { name: /Registrar salida/ })).toBeDisabled();
    });

    it('FAC-08 Fase 1: la salida de un contenedor cancelado se ve descartada y no se ofrece facturar', async () => {
      setPermisos(permisosCompletos);
      prepararPedido(
        [
          { id: 's-1', numeroParcialidad: 1, cantidad: 1, importe: 100, origen: 2, referencia: 'DEMO-CONT-0001' },
          {
            id: 's-2', numeroParcialidad: 2, cantidad: 1, importe: 100, origen: 2, referencia: 'DEMO-CONT-0002',
            descartadaEn: '2026-11-05T18:00:00Z', motivoDescarte: 'Contenedor cancelado en Comercio Exterior',
          },
        ],
        1,
        0,
      );
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      const descartada = await screen.findByTestId('salida-2');
      expect(within(descartada).getByText('Descartada')).toBeInTheDocument();
      expect(within(descartada).getByText(/Contenedor cancelado en Comercio Exterior/)).toBeInTheDocument();
      expect(within(descartada).queryByText('Por facturar')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Facturar la parcialidad 2' })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Facturar la parcialidad 1' })).toBeInTheDocument();
    });

    it('facturar una parcialidad precarga solo sus cantidades y la liga a la salida', async () => {
      setPermisos(permisosCompletos);
      prepararPedido([{ id: 's-2', numeroParcialidad: 2, cantidad: 1, importe: 95 }], 1, 0);
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      fireEvent.click(await screen.findByRole('button', { name: 'Facturar la parcialidad 2' }));
      expect(await screen.findByTestId('emitir-factura-form')).toBeInTheDocument();
      expect(screen.getByText(/parcialidad 2/)).toBeInTheDocument();
      const prefill = prefillSpy.mock.calls.at(-1)?.[0] as {
        salidaFacturableId: string;
        numeroParcialidad: number;
        lineas: Array<{ cantidad: number; valorUnitario: number; descuento: number }>;
      };
      expect(prefill.salidaFacturableId).toBe('s-2');
      expect(prefill.numeroParcialidad).toBe(2);
      // 1 pieza × $100 con importe de la salida $95 → descuento $5.
      expect(prefill.lineas).toEqual([
        expect.objectContaining({ cantidad: 1, valorUnitario: 100, descuento: 5 }),
      ]);
    });

    it('FAC-08 Fase 1: la salida de un contenedor con el pedido al 50 % usa la compuerta de ese contenedor', async () => {
      setPermisos(permisosCompletos);
      const embarque = (listo: boolean, contenedores: string[]) => ({
        ligado: true, expedienteId: 'e-1', sucursalId: null, listoParaFacturar: listo,
        codigoBloqueo: listo ? null : 'FAC_PEDIDO_NO_EMBARCADO',
        motivo: listo ? null : 'Falta embarcar el 100 % del pedido: posición 1: embarcado 1 de 2 H87',
        folioExpediente: 'CE-EXP-1', numeroPedimento: '26 47 3000 6000001', clavePedimento: 'A1',
        fechaPedimento: '2026-11-05', incoterm: 'FOB',
        contenedores: contenedores.map((numero) => ({ id: numero, numero, blGuia: 'BL', fechaEmbarqueReal: '2026-11-06' })),
      });
      const salidasPedidas: Array<string | null> = [];
      prepararPedido(
        [{ id: 's-1', numeroParcialidad: 1, cantidad: 1, importe: 100, origen: 2, referencia: 'QA-C1-0001' }],
        1,
        0,
      );
      mswServer.use(
        http.get('*/api/v1/facturacion/pedidos-facturables/p-1', ({ request }) => {
          const salidaId = new URL(request.url).searchParams.get('salidaId');
          salidasPedidas.push(salidaId);
          return HttpResponse.json({
            ...pedido('Importado'),
            // Pedido al 50 %: completo, bloqueado; con la salida, solo su contenedor y listo.
            embarqueCe: salidaId === 's-1' ? embarque(true, ['QA-C1-0001']) : embarque(false, ['QA-C1-0001']),
          });
        }),
      );
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      fireEvent.click(await screen.findByRole('button', { name: 'Facturar la parcialidad 1' }));
      expect(await screen.findByTestId('emitir-factura-form')).toBeInTheDocument();
      const prefill = prefillSpy.mock.calls.at(-1)?.[0] as {
        salidaFacturableId: string;
        embarqueCe: { listoParaFacturar: boolean; contenedores: Array<{ numero: string }> };
      };
      expect(prefill.salidaFacturableId).toBe('s-1');
      expect(prefill.embarqueCe.listoParaFacturar).toBe(true);
      expect(prefill.embarqueCe.contenedores.map((c) => c.numero)).toEqual(['QA-C1-0001']);
      expect(salidasPedidas).toContain('s-1');
    });

    it('registra una salida con la cantidad capturada y llave de idempotencia', async () => {
      setPermisos(permisosCompletos);
      prepararPedido([], 0, 0);
      let body: unknown = null;
      let llave: string | null = null;
      mswServer.use(
        http.post('*/api/v1/facturacion/pedidos-facturables/p-1/salidas', async ({ request }) => {
          body = await request.json();
          llave = request.headers.get('Idempotency-Key');
          return HttpResponse.json(
            { id: 's-1', pedidoFacturableId: 'p-1', numeroParcialidad: 1, origen: 3, referencia: 'SAL-A', total: 100, yaRegistrada: false, lineas: [] },
            { status: 201 },
          );
        }),
      );
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      fireEvent.click(await screen.findByRole('button', { name: /Registrar salida/ }));
      const dialogo = await screen.findByRole('dialog');
      const registrar = within(dialogo).getByRole('button', { name: 'Registrar salida' });
      expect(registrar).toBeDisabled();
      fireEvent.change(within(dialogo).getByLabelText(/Folio de salida o contenedor/), {
        target: { value: ' SAL-A ' },
      });
      fireEvent.change(
        within(dialogo).getByLabelText('Cantidad de la partida 1 en esta salida'),
        { target: { value: '1' } },
      );
      expect(registrar).toBeEnabled();
      fireEvent.click(registrar);
      await waitFor(() => expect(body).not.toBeNull());
      expect(body).toEqual({ referencia: 'SAL-A', lineas: [{ posicion: 1, cantidad: 1 }] });
      expect(llave).toBeTruthy();
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('no deja registrar más de lo pendiente de salir', async () => {
      setPermisos(permisosCompletos);
      prepararPedido([{ id: 's-1', numeroParcialidad: 1, cantidad: 1, importe: 100 }], 1, 0);
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      fireEvent.click(await screen.findByRole('button', { name: /Registrar salida/ }));
      const dialogo = await screen.findByRole('dialog');
      fireEvent.change(within(dialogo).getByLabelText(/Folio de salida o contenedor/), {
        target: { value: 'SAL-B' },
      });
      fireEvent.change(
        within(dialogo).getByLabelText('Cantidad de la partida 1 en esta salida'),
        { target: { value: '2' } },
      );
      expect(within(dialogo).getByText('Máximo 1.')).toBeInTheDocument();
      expect(within(dialogo).getByRole('button', { name: 'Registrar salida' })).toBeDisabled();
    });

    it('sin permiso de captura no ofrece registrar salidas', async () => {
      setPermisos(['facturacion.facturas.leer', 'facturacion.facturas.emitir']);
      prepararPedido([], 0, 0);
      render(<DetallePedido />, { wrapper: createQueryWrapper() });
      expect(await screen.findByText(/Salidas parciales \(0\)/)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /Registrar salida/ })).not.toBeInTheDocument();
    });
  });
});
