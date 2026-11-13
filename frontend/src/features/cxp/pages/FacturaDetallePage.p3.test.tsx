import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { FacturaDetallePage } from './FacturaDetallePage';
import { esContado } from '../lib/es-contado';
import { EstadoPasivo, MotivoCancelacion } from '@/features/cxp/api/types';
import { createQueryWrapper } from '@/test/test-query-client';

const escenario = vi.hoisted(() => ({ revision: false, cancelada: false, contado: false, ce: false, n2: false, clave: undefined as string | null | undefined }));
vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'factura-ficticia-p3' }),
  useSearch: () => ({}),
  Link: ({
    children,
    to,
    params,
  }: {
    children: ReactNode;
    to: string;
    params?: { id: string };
  }) => <a href={params ? to.replace('$id', params.id) : to}>{children}</a>,
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/features/cxp/api/useFacturas', () => ({
  useEditarCabeceraFactura: () => ({ isPending: false, mutateAsync: vi.fn() }),
  useAutorizarFactura: () => ({ isPending: false, mutate: vi.fn() }),
  useMarcarRetencionesRevisadas: () => ({ isPending: false, mutateAsync: vi.fn() }),
  useFactura: () => ({
    isLoading: false,
    isError: false,
    data: {
      id: 'factura-ficticia-p3',
      proveedorId: 'proveedor',
      proveedorNombre: escenario.clave === undefined ? null : 'Proveedor de prueba',
      proveedorClave: escenario.clave,
      sucursalId: 'sucursal',
      folioProveedor: 'P3-FICTICIA',
      serieProveedor: null,
      estado: escenario.revision ? EstadoPasivo.EnRevision : escenario.cancelada ? EstadoPasivo.Cancelada : escenario.ce ? EstadoPasivo.Capturada : EstadoPasivo.Autorizada,
      esServicioComercioExterior: escenario.ce,
      servicioCeSegundaFirma: escenario.n2,
      fechaDocumento: '2026-10-09T18:00:00Z',
      fechaContabilizacion: '2026-10-09',
      fechaVencimiento: escenario.contado ? '2026-10-09' : '2026-11-09',
      moneda: 'MXN',
      tipoCambio: null,
      ordenCompraId: '11111111-1111-4111-8111-111111111111',
      ordenCompraFolio: 'OC-DEMO2026-000101',
      uuidCfdi: null,
      subtotal: 200,
      descuentos: 0,
      impuestosTrasladados: 32,
      retenciones: 33.33,
      total: 198.67,
      anticipoAplicadoTotal: 0,
      ncAplicadasTotal: 0,
      importePagado: 0,
      saldoPendiente: 198.67,
      elegible: 158.94,
      retenido: 39.73,
      diferenciaContraOc: 0,
      enRevision: escenario.revision,
      revisionRepp: escenario.revision ? {
        pendiente: true, ultimaAutorizacionEn: '2026-11-03T23:10:56Z',
        pagos: [{ pagoId: 'pago-PRB-ficticio', fechaPago: '2026-11-03', importe: 8700, cubiertoRepp: 0, moneda: 'MXN' }],
      } : null,
      version: 1,
      lineas: [],
      retencionesDetalle: [
        { impuesto: '001', tasa: null, importe: 20 },
        { impuesto: '002', tasa: null, importe: 13.33 },
      ],
      motivoCancelacion: escenario.cancelada ? MotivoCancelacion.RechazadaPorTolerancia : null,
      motivoCancelacionTexto: escenario.cancelada
        ? 'Precio distinto en la línea 1: OC 20; factura 50.'
        : null,
    },
  }),
}));
vi.mock('@/features/cxp/components/CancelarFacturaSheet', () => ({
  CancelarFacturaSheet: () => null,
}));
vi.mock('@/features/cxp/components/EnviarRevisionSheet', () => ({
  EnviarRevisionSheet: () => null,
}));
vi.mock('@/features/cxp/components/LiberarRevisionSheet', () => ({
  LiberarRevisionSheet: () => null,
}));
vi.mock('@/features/cxp/components/AdjuntarEvidenciaSheet', () => ({
  AdjuntarEvidenciaSheet: () => null,
}));
vi.mock('@/features/cxp/components/EvidenciasList', () => ({ EvidenciasList: () => null }));

beforeEach(() => {
  escenario.revision = false; escenario.ce = false; escenario.n2 = false; escenario.clave = undefined;
  mswServer.use(
    http.get("*/api/v1/contabilidad/polizas/por-documento", () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 10 })),
    http.get('*/api/v1/cuentas-por-pagar/facturas/factura-ficticia-p3/adjuntos', () =>
      HttpResponse.json([]),
    ),
    http.get('*/api/v1/adjuntos/tipos', () => HttpResponse.json([])),
  );
});

function importe(label: string) {
  return screen.getByText(label).nextElementSibling?.textContent;
}

describe('P3: detalle fiscal y recepción', () => {
  it('muestra el folio enlazado a la OC y no muestra su GUID ni IDs de catálogos', () => {
    escenario.cancelada = false;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('link', { name: 'OC-DEMO2026-000101' })).toHaveAttribute(
      'href',
      '/compras/ordenes/11111111-1111-4111-8111-111111111111',
    );
    expect(screen.queryByText('11111111-1111-4111-8111-111111111111')).not.toBeInTheDocument();
    expect(screen.getByText('Sin proveedor')).toBeInTheDocument();
    expect(screen.getByText('Sin sucursal')).toBeInTheDocument();
  });
  it('presenta ISR, IVA retenido, total, elegible y retenido por separado', async () => {
    escenario.cancelada = false;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(importe('ISR retenido')).toContain('20.00');
    expect(importe('IVA retenido')).toContain('13.33');
    expect(importe('Elegible para pago')).toContain('158.94');
    expect(importe('Retenido por recepción pendiente')).toContain('39.73');
    expect(importe('Total')).toContain('198.67');
    expect(await screen.findByRole('region', { name: 'Adjuntos' })).toBeInTheDocument();
  });
  it('aclara "contado" solo cuando el vencimiento coincide con la fecha del documento', () => {
    escenario.cancelada = false;
    escenario.contado = true;
    const { unmount } = render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/contado, sin días de crédito/)).toBeInTheDocument();
    unmount();
    escenario.contado = false;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.queryByText(/contado, sin días de crédito/)).not.toBeInTheDocument();
  });
  it('reconoce contado cuando el documento llega a medianoche UTC', () => {
    expect(esContado('2026-11-01', '2026-11-01T00:00:00Z')).toBe(true);
    expect(esContado('2026-10-09', '2026-10-09T18:00:00Z')).toBe(true);
    expect(esContado('2026-12-01', '2026-11-01T00:00:00Z')).toBe(false);
  });
  it('muestra la línea y el dato rechazado y no permite forzar la autorización', () => {
    escenario.cancelada = true;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/Precio distinto en la línea 1/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });
});


it('pasivo CE espera a Dirección Comercial antes de enviarlo a Tesorería', () => {
  escenario.ce = true; escenario.cancelada = false; escenario.n2 = false;
  const { unmount } = render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
  expect(screen.getByRole('button', { name: 'Enviar a Tesorería' })).toBeDisabled();
  expect(screen.getByText(/segunda firma de Dirección Comercial/)).toBeInTheDocument();
  expect(screen.getByText('Retenido por autorización de servicio')).toBeInTheDocument();
  expect(screen.queryByText('Retenido por recepción pendiente')).not.toBeInTheDocument();
  unmount(); escenario.n2 = true;
  render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
  expect(screen.getByRole('button', { name: 'Enviar a Tesorería' })).toBeEnabled();
});

describe('A21 identidad de proveedor en detalle', () => {
  it.each(['PROV-001', 'PROV-002', null])('muestra clave %s y nombre al consultar factura', (clave) => {
    escenario.clave = clave;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(clave ?? 'Sin código')).toBeVisible();
    expect(screen.getByText(/Proveedor de prueba/)).toBeVisible();
  });
});


it('integra la póliza del proveedor mediante su identidad canónica', async () => {
  escenario.cancelada = false; escenario.ce = false;
  const consultas: URL[] = [];
  mswServer.use(http.get('*/api/v1/contabilidad/polizas/por-documento', ({ request }) => {
    consultas.push(new URL(request.url));
    return HttpResponse.json({ items: [{ id: 'poliza-p-21', folio: 'EG-000021', fecha: '2026-11-12', estado: 'Autorizada', polizaAnuladaId: null, anuladaPorPolizaId: null, polizaRelacionadaId: null, relacion: null }], total: 1, offset: 0, limit: 10 });
  }));
  render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByRole('link', { name: 'EG-000021' })).toHaveAttribute('href', '/contabilidad/polizas/poliza-p-21');
  expect(consultas.length).toBeGreaterThan(0);
  expect(consultas[0].searchParams.get('modulo')).toBe('cuentas_por_pagar');
  expect(consultas[0].searchParams.get('tipo')).toBe('factura_proveedor');
  expect(consultas[0].searchParams.get('documentoId')).toBe('factura-ficticia-p3');
});

it('FB15 explica revisión posterior al pago y dirige al REPP sin volver a autorizar', () => {
  escenario.revision = true; escenario.cancelada = false;
  render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
  expect(screen.getByText(/autorización anterior/i)).toBeInTheDocument();
  expect(screen.getByText(/8,700.00/)).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /Abrir Tesorería → REPP de proveedor/ })).toHaveAttribute('href', '/tesoreria/repp');
  expect(screen.getByRole('button', { name: 'Autorizar' })).toBeDisabled();
});
