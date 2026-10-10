import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { FacturaDetallePage } from './FacturaDetallePage';
import { EstadoPasivo, MotivoCancelacion } from '@/features/cxp/api/types';
import { createQueryWrapper } from '@/test/test-query-client';

const escenario = vi.hoisted(() => ({ cancelada: false }));
vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'factura-ficticia-p3' }),
  Link: ({ children, to, params }: { children: ReactNode; to: string; params?: { id: string } }) =>
    <a href={params ? to.replace('$id', params.id) : to}>{children}</a>,
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/features/cxp/api/useFacturas', () => ({
  useAutorizarFactura: () => ({ isPending: false, mutate: vi.fn() }),
  useFactura: () => ({ isLoading: false, isError: false, data: {
    id: 'factura-ficticia-p3', proveedorId: 'proveedor', sucursalId: 'sucursal',
    folioProveedor: 'P3-FICTICIA', serieProveedor: null, estado: escenario.cancelada ? EstadoPasivo.Cancelada : EstadoPasivo.Autorizada,
    fechaDocumento: '2026-10-09', fechaContabilizacion: '2026-10-09', fechaVencimiento: '2026-11-09',
    moneda: 'MXN', tipoCambio: null, ordenCompraId: '11111111-1111-4111-8111-111111111111', ordenCompraFolio: 'OC-DEMO2026-000101', uuidCfdi: null,
    subtotal: 200, descuentos: 0, impuestosTrasladados: 32, retenciones: 33.33, total: 198.67,
    anticipoAplicadoTotal: 0, ncAplicadasTotal: 0, importePagado: 0, saldoPendiente: 198.67,
    elegible: 158.94, retenido: 39.73, diferenciaContraOc: 0, enRevision: false, version: 1, lineas: [],
    retencionesDetalle: [{ impuesto: '001', tasa: null, importe: 20 }, { impuesto: '002', tasa: null, importe: 13.33 }],
    motivoCancelacion: escenario.cancelada ? MotivoCancelacion.RechazadaPorTolerancia : null,
    motivoCancelacionTexto: escenario.cancelada ? 'Precio distinto en la línea 1: OC 20; factura 50.' : null,
  } }),
}));
vi.mock('@/features/cxp/components/CancelarFacturaSheet', () => ({ CancelarFacturaSheet: () => null }));
vi.mock('@/features/cxp/components/EnviarRevisionSheet', () => ({ EnviarRevisionSheet: () => null }));
vi.mock('@/features/cxp/components/LiberarRevisionSheet', () => ({ LiberarRevisionSheet: () => null }));
vi.mock('@/features/cxp/components/AdjuntarEvidenciaSheet', () => ({ AdjuntarEvidenciaSheet: () => null }));
vi.mock('@/features/cxp/components/EvidenciasList', () => ({ EvidenciasList: () => null }));

beforeEach(() => {
  mswServer.use(
    http.get('/api/v1/cuentas-por-pagar/facturas/factura-ficticia-p3/adjuntos', () =>
      HttpResponse.json([])),
    http.get('/api/v1/adjuntos/tipos', () => HttpResponse.json([])),
  );
});

function importe(label: string) { return screen.getByText(label).nextElementSibling?.textContent; }

describe('P3: detalle fiscal y recepción', () => {
  it('muestra el folio enlazado a la OC y no muestra su GUID ni IDs de catálogos', () => {
    escenario.cancelada = false;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('link', { name: 'OC-DEMO2026-000101' }))
      .toHaveAttribute('href', '/compras/ordenes/11111111-1111-4111-8111-111111111111');
    expect(screen.queryByText('11111111-1111-4111-8111-111111111111')).not.toBeInTheDocument();
    expect(screen.getByText('[PROVEEDOR POR CONFIRMAR]')).toBeInTheDocument();
    expect(screen.getByText('[SUCURSAL POR CONFIRMAR]')).toBeInTheDocument();
  });
  it('presenta ISR, IVA retenido, total, elegible y retenido por separado', async () => {
    escenario.cancelada = false;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(importe('ISR retenido')).toContain('20.00');
    expect(importe('IVA retenido')).toContain('13.33');
    expect(importe('Elegible para pago')).toContain('158.94');
    expect(importe('Retenido por falta de recepción')).toContain('39.73');
    expect(importe('Total')).toContain('198.67');
    expect(await screen.findByRole('region', { name: 'Adjuntos' })).toBeInTheDocument();
  });
  it('muestra la línea y el dato rechazado y no permite forzar la autorización', () => {
    escenario.cancelada = true;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/Precio distinto en la línea 1/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });
});
