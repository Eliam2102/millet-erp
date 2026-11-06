import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LiquidacionIvaPage } from './LiquidacionIvaPage';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const API = '*/api/v1/contabilidad/iva/liquidacion';

function documento(o: Record<string, unknown>) {
  return {
    saldoId: 's', lado: 'Acreditable', origenTipo: 'factura_proveedor', folio: null, uuid: 'UUID-FIX', metodoPago: 'PUE',
    fecha: '2026-11-02', moneda: 'MXN', base: 1160, iva: 160, aplicadoMes: 1160, ivaMes: 160, ivaRetenidoMes: 0, ivaPendiente: 0,
    saldo: 0, completo: true, motivos: [], ...o,
  };
}

function liquidacion(cuadra = true) {
  return {
    anio: 2026, mes: 11,
    resumen: {
      ivaTrasladadoCobrado: 320, ivaAcreditablePagado: 960, ivaRetenidoPorClientes: 0, ivaRetenidoAProveedores: 40, ivaACargo: 0,
      ivaAFavor: 640,
    },
    cuadre: [
      { rol: 'IVA_TRASLADADO_COBRADO', descripcion: 'IVA trasladado cobrado', cuentas: ['FIX-2.6'], porDocumentos: 320, enCuentas: 320,
        diferencia: 0, cuadra: true },
      { rol: 'IVA_ACREDITABLE_PAGADO', descripcion: 'IVA acreditable pagado', cuentas: ['FIX-1.0'], porDocumentos: 960,
        enCuentas: cuadra ? 960 : 1000, diferencia: cuadra ? 0 : 40, cuadra },
    ],
    cuadra,
    documentos: [
      documento({ documentoId: 'd1', folio: 'FP-FIX-1' }),
      documento({ documentoId: 'd2', folio: 'FP-FIX-2', iva: 1600, aplicadoMes: 5800, ivaMes: 800, ivaPendiente: 800, saldo: 5800,
        completo: false, motivos: ['Con saldo por pagar'] }),
      documento({ documentoId: 'd3', folio: 'FV-FIX-3', lado: 'Trasladado', metodoPago: 'PPD', iva: 320, aplicadoMes: 2320, ivaMes: 320,
        completo: false, motivos: ['PPD pagado sin REP'] }),
    ],
    pendientes: 2,
  };
}

function servidor(datos = liquidacion()) {
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () => HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 })),
    http.get(API, () => HttpResponse.json(datos)),
  );
}

describe('LiquidacionIvaPage (K10.7)', () => {
  it('muestra el resumen del mes con el IVA a favor y la retención a proveedores aparte', async () => {
    servidor();
    render(<LiquidacionIvaPage />, { wrapper: createQueryWrapper() });
    const resumen = await screen.findByRole('region', { name: 'Resumen del mes' });
    expect(within(resumen).getByText('IVA a favor')).toBeInTheDocument();
    expect(within(resumen).getByText('$640.00')).toBeInTheDocument();
    expect(within(resumen).getByText('−$960.00')).toBeInTheDocument();
    expect(within(resumen).getByText('IVA retenido a proveedores (por enterar aparte)')).toBeInTheDocument();
    // Sin sucursal el borrador no se puede crear y el botón dice por qué.
    expect(within(resumen).getByRole('button', { name: 'Crear borrador de póliza' })).toBeDisabled();
  });

  it('marca el cumplimiento de cada documento con su motivo (K10.7-b)', async () => {
    servidor();
    render(<LiquidacionIvaPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Pendiente: PPD pagado sin REP')).toBeInTheDocument();
    expect(screen.getByText('Pendiente: Con saldo por pagar')).toBeInTheDocument();
    expect(screen.getByText('Solo pendientes (2)')).toBeInTheDocument();
  });

  it('avisa cuando el IVA por documentos no cuadra con las cuentas (K10.7-a)', async () => {
    servidor(liquidacion(false));
    render(<LiquidacionIvaPage />, { wrapper: createQueryWrapper() });
    const cuadre = await screen.findByRole('region', { name: 'Cuadre contra cuentas' });
    expect(within(cuadre).getByText('No cuadra')).toBeInTheDocument();
    expect(within(cuadre).getByText('$40.00')).toBeInTheDocument();
    expect(within(cuadre).getByRole('note')).toHaveTextContent('no vienen de un pago o cobro');
  });
});
