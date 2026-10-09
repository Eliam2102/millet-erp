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
  Link: ({ children }: { children: ReactNode }) => <a href="/cxp/facturas">{children}</a>,
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@/features/cxp/api/useFacturas', () => ({
  useAutorizarFactura: () => ({ isPending: false, mutate: vi.fn() }),
  useFactura: () => ({
    isLoading: false,
    isError: false,
    data: {
      id: 'factura-ficticia-p3',
      proveedorId: 'proveedor',
      sucursalId: 'sucursal',
      folioProveedor: 'P3-FICTICIA',
      serieProveedor: null,
      estado: escenario.cancelada ? EstadoPasivo.Cancelada : EstadoPasivo.Autorizada,
      fechaDocumento: '2026-10-09',
      fechaContabilizacion: '2026-10-09',
      fechaVencimiento: '2026-11-09',
      moneda: 'MXN',
      tipoCambio: null,
      ordenCompraId: 'oc',
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
      enRevision: false,
      version: 1,
      lineas: [],
      obra: 'FIX-OBRA-A',
      conceptoRetencion: 'HONORARIOS_PF',
      alertaRetenciones:
        'Las retenciones del CFDI difieren del catálogo. Supuesto SAT, valida Fiscal (D03).',
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
  mswServer.use(
    http.get('/api/v1/cuentas-por-pagar/facturas/factura-ficticia-p3/adjuntos', () =>
      HttpResponse.json([])),
    http.get('/api/v1/adjuntos/tipos', () => HttpResponse.json([])),
  );
});

function importe(label: string) {
  return screen.getByText(label).nextElementSibling?.textContent;
}

describe('P8: alerta fiscal y obra en detalle', () => {
  it('muestra obra y alerta del CFDI sin ocultar la factura', async () => {
    escenario.cancelada = false;
    render(<FacturaDetallePage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('FIX-OBRA-A')).toBeInTheDocument();
    expect(screen.getByText(/Las retenciones del CFDI difieren del catálogo/)).toBeInTheDocument();
    expect(importe('Saldo pendiente')).toContain('198.67');
    expect(await screen.findByRole('region', { name: 'Adjuntos' })).toBeInTheDocument();
  });
});
