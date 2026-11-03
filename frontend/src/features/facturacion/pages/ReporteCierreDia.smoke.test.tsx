import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteCierreDia } from '@/features/facturacion/pages/ReporteCierreDia';

const SUC = '11111111-1111-4111-8111-111111111111';
let searchActual: Record<string, unknown> = {};

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => searchActual,
  Link: ({ children }: { children: ReactNode }) => <a href="#">{children}</a>,
}));

function reporte(titulo: string, columnas: unknown[], filas: Record<string, unknown>[]) {
  return {
    titulo,
    generadoEn: '2026-10-21T18:00:00Z',
    filtrosAplicados: {},
    columnas,
    filas,
    totales: null,
  };
}

function conciliacionStub(cuadra: boolean) {
  const diferencia = cuadra ? 0 : 1160; // porCobrar − ingresado
  return {
    sucursal: 'Matriz',
    dia: '2026-10-21',
    cajero: null,
    resumen: {
      facturadoContado: 2320,
      facturasContado: 2,
      notasCredito: 0,
      porCobrar: 2320,
      ingresado: cuadra ? 2320 : 1160,
      ingresadoEfectivo: cuadra ? 2320 : 1160,
      ingresadoTpv: 0,
      ingresadoTransferencia: 0,
      ingresadoOtras: 0,
      diferencia,
      cuadra,
      facturadoCredito: 0,
      facturasCredito: 0,
    },
    porCajero: reporte(
      'Por cajero',
      [
        { clave: 'cajero', etiqueta: 'Cajero', tipo: 'texto' },
        { clave: 'diferencia', etiqueta: 'Diferencia', tipo: 'moneda' },
        { clave: 'estado', etiqueta: 'Estado', tipo: 'texto' },
      ],
      [
        {
          cajeroId: null,
          cajero: 'Ana Cajera',
          partidas: 2,
          porCobrar: 2320,
          ingresado: cuadra ? 2320 : 1160,
          diferencia,
          estado: cuadra ? 'Cuadra' : 'Con diferencia',
        },
      ],
    ),
    partidas: reporte(
      'Partidas',
      [
        { clave: 'comprobante', etiqueta: 'Comprobante', tipo: 'texto' },
        { clave: 'situacion', etiqueta: 'Situación', tipo: 'texto' },
        { clave: 'diferencia', etiqueta: 'Diferencia', tipo: 'moneda' },
      ],
      [
        {
          comprobanteId: 'c-1',
          fecha: '2026-10-21T10:00:00',
          comprobante: 'Factura FV-101',
          cliente: 'Cliente uno',
          metodoPago: 'PUE',
          cajero: 'Ana Cajera',
          situacion: cuadra ? 'Cuadra' : 'Facturada sin cobro',
          facturado: 1160,
          notasCredito: 0,
          porCobrar: 1160,
          ingresado: cuadra ? 1160 : 0,
          diferencia: cuadra ? 0 : 1160,
          formasPago: cuadra ? 'Efectivo' : null,
        },
      ],
    ),
  };
}

describe('<ReporteCierreDia> — smoke', () => {
  beforeEach(() => {
    searchActual = {};
  });

  it('sin sucursal pide elegirla y no consulta', () => {
    render(<ReporteCierreDia />, { wrapper: createQueryWrapper() });
    expect(screen.getAllByText(/Elige una sucursal/).length).toBeGreaterThan(0);
  });

  it('pinta los KPIs del monitor y la fuente de verificación', async () => {
    searchActual = { sucursalId: SUC, dia: '2026-10-21' };
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/monitor-cfdi', () =>
        HttpResponse.json({
          sucursal: 'Matriz',
          dia: '2026-10-21',
          cajero: null,
          fuenteSat: 'Simulado (adaptador de prueba)',
          resumen: {
            timbrados: 7,
            cancelados: 2,
            cancelacionEnTramite: 1,
            sinTimbrar: 3,
            fallidos: 1,
            pedidosSinTimbrar: 5,
            sinVerificarSat: 6,
            discrepancias: 1,
            totalVigente: 12000,
          },
          comprobantes: reporte(
            'Comprobantes del día',
            [
              { clave: 'folio', etiqueta: 'Folio', tipo: 'texto' },
              { clave: 'estado', etiqueta: 'Estado', tipo: 'texto' },
              { clave: 'estatusSat', etiqueta: 'Estatus SAT', tipo: 'texto' },
            ],
            [
              {
                comprobanteId: 'c-1',
                fecha: '2026-10-21T10:00:00',
                tipo: 'Factura',
                folio: 'FV-101',
                uuid: 'AAAA-BBBB',
                cliente: 'Cliente uno',
                metodoPago: 'PUE',
                moneda: 'MXN',
                total: 1160,
                estado: 'Timbrado',
                estatusSat: 'Cancelado',
                estatusSatConsultadoEn: '2026-10-21T17:00:00Z',
                discrepancia: true,
                cajero: 'Ana Cajera',
                detalle: null,
              },
            ],
          ),
          pedidosSinTimbrar: reporte(
            'Pedidos sin timbrar',
            [{ clave: 'pedido', etiqueta: 'Pedido', tipo: 'texto' }],
            [],
          ),
        }),
      ),
    );
    render(<ReporteCierreDia />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FV-101')).toBeInTheDocument());
    expect(screen.getByText('Timbrados')).toBeInTheDocument();
    expect(screen.getByText('7')).toBeInTheDocument();
    // Sin timbrar = sinTimbrar + fallidos.
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('Pedidos sin timbrar', { selector: 'span' })).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.getByText(/Fuente: Simulado \(adaptador de prueba\)/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Verificar en SAT/ })).toBeInTheDocument();
    // Estatus SAT distinto al local → badge de discrepancia con texto.
    expect(screen.getByText('Discrepancia')).toBeInTheDocument();
  });

  it('en conciliación con diferencia muestra la situación y la diferencia', async () => {
    searchActual = { sucursalId: SUC, dia: '2026-10-21', vista: 'conciliacion' };
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/conciliacion-facturado-ingresado', () =>
        HttpResponse.json(conciliacionStub(false)),
      ),
    );
    render(<ReporteCierreDia />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Facturada sin cobro')).toBeInTheDocument(),
    );
    expect(screen.getByText(/Diferencia de \$1,160\.00/)).toBeInTheDocument();
  });

  it('crédito emitido cuenta todas las PPD y aclara las de otra moneda, con plural correcto', async () => {
    searchActual = { sucursalId: SUC, dia: '2026-10-21', vista: 'conciliacion' };
    const base = conciliacionStub(true);
    const ppd = (folio: string) => ({
      ...base.partidas.filas[0],
      comprobanteId: folio,
      comprobante: `Factura ${folio}`,
      situacion: 'Crédito (PPD) · informativo',
      porCobrar: 0,
      ingresado: 0,
      diferencia: 0,
    });
    const stub = {
      ...base,
      resumen: { ...base.resumen, facturasContado: 1, facturadoCredito: 1740, facturasCredito: 1 },
      partidas: { ...base.partidas, filas: [...base.partidas.filas, ppd('PPD-MXN'), ppd('PPD-USD')] },
    };
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/conciliacion-facturado-ingresado', () => HttpResponse.json(stub)),
    );
    render(<ReporteCierreDia />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText('2 facturas PPD (1 en otra moneda no suma al total en MXN); no entran a la diferencia.'),
    ).toBeInTheDocument();
    expect(screen.getByText(/^1 factura por /)).toBeInTheDocument();
  });

  it('en conciliación cuadrada muestra «Cuadra»', async () => {
    searchActual = { sucursalId: SUC, dia: '2026-10-21', vista: 'conciliacion' };
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/conciliacion-facturado-ingresado', () =>
        HttpResponse.json(conciliacionStub(true)),
      ),
    );
    render(<ReporteCierreDia />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByTestId('indicador-conciliacion')).toHaveTextContent('Cuadra'),
    );
  });
});
