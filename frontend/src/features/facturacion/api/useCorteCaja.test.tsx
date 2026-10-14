import { describe, expect, it, vi } from 'vitest';
import { render, renderHook, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useCorteCaja } from '@/features/facturacion/api/useCajaSesiones';
import type { CorteCajaResponse } from '@/features/facturacion/api/types';
import { ReporteCorteCaja } from '@/features/facturacion/pages/ReporteCorteCaja';

vi.mock('@tanstack/react-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tanstack/react-router')>()),
  useParams: () => ({ sesionId: 'ses-1' }),
}));

const corteStub: CorteCajaResponse = {
  encabezado: {
    sesionId: 'ses-1',
    caja: 'Caja Mostrador',
    sucursal: 'MID — Mérida',
    cajeroUsuarioId: 'usr-1',
    cajero: 'Ana Cajera',
    estado: 'Cerrada',
    diaOperacion: '2026-10-13',
    fechaApertura: '2026-10-13T14:00:00Z',
    fechaCierre: '2026-10-13T23:00:00Z',
    fondoApertura: 500,
    efectivoEsperado: 1544,
    efectivoContado: 1544,
    diferencia: 0,
    cierreExtemporaneo: false,
    notasCierre: null,
  },
  porFormaPago: [
    { formaPago: '01', descripcion: 'Efectivo', cobros: 1, montoSistema: 1544, montoDeclarado: 1544 },
  ],
  reporte: {
    titulo: 'Corte de caja · Caja Mostrador · 2026-10-13',
    generadoEn: '2026-10-13T23:01:00Z',
    filtrosAplicados: { Cajero: 'Ana Cajera' },
    columnas: [{ clave: 'importe', etiqueta: 'Importe en caja', tipo: 'moneda' }],
    filas: [],
    totales: { importe: 1544 },
  },
  resumen: {
    facturado: 3480,
    facturas: 2,
    notasCredito: 116,
    notas: 1,
    facturadoNeto: 3364,
    cobranzas: 1044,
    apartados: 0,
    tpv: 0,
    totalIngresado: 1044,
  },
  facturado: {
    titulo: 'Facturado · Caja Mostrador · 2026-10-13',
    generadoEn: '2026-10-13T23:01:00Z',
    filtrosAplicados: { Cajero: 'Ana Cajera' },
    columnas: [
      { clave: 'comprobante', etiqueta: 'Comprobante', tipo: 'texto' },
      { clave: 'cobro', etiqueta: 'Cobro', tipo: 'texto' },
      { clave: 'total', etiqueta: 'Total', tipo: 'moneda' },
    ],
    filas: [
      {
        fecha: '13/10/2026 10:00', comprobante: 'Factura F-101', cliente: 'Cliente Mostrador',
        metodoPago: 'PUE', moneda: 'MXN', estadoCfdi: 'Timbrado', cobro: 'Cobrada en esta sesión', total: 1160,
      },
      {
        fecha: '13/10/2026 11:00', comprobante: 'Factura F-103', cliente: 'Cliente Ruta',
        metodoPago: 'PPD', moneda: 'MXN', estadoCfdi: 'Timbrado', cobro: 'Crédito (PPD)', total: 2320,
      },
    ],
    totales: { total: 3480 },
  },
  notasCredito: {
    titulo: 'Notas de crédito · Caja Mostrador · 2026-10-13',
    generadoEn: '2026-10-13T23:01:00Z',
    filtrosAplicados: { Cajero: 'Ana Cajera' },
    columnas: [
      { clave: 'folio', etiqueta: 'Folio', tipo: 'texto' },
      { clave: 'motivo', etiqueta: 'Motivo', tipo: 'texto' },
      { clave: 'total', etiqueta: 'Total', tipo: 'moneda' },
    ],
    filas: [
      {
        fecha: '13/10/2026 10:00', folio: 'NC-R1', motivo: 'Ranura', facturaRelacionada: 'F-101',
        cliente: 'Cliente Mostrador', moneda: 'MXN', estadoCfdi: 'Timbrado', total: 116,
      },
    ],
    totales: { total: 116 },
  },
};

describe('useCorteCaja (FIX-12)', () => {
  it('consulta el corte de la sesión', async () => {
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/cajas/sesiones/:id/corte', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json(corteStub);
      }),
    );
    const { result } = renderHook(() => useCorteCaja('ses-1'), { wrapper: createQueryWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVista).toContain('/cajas/sesiones/ses-1/corte');
    expect(result.current.data?.encabezado.cajero).toBe('Ana Cajera');
  });

  it('no dispara sin sesión', () => {
    const { result } = renderHook(() => useCorteCaja(''), { wrapper: createQueryWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('ReporteCorteCaja (FIX-12, demo 12-oct M19)', () => {
  it('muestra el resumen de la entrega y las secciones de facturado y notas de crédito', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/cajas/sesiones/:id/corte', () => HttpResponse.json(corteStub)),
    );
    render(<ReporteCorteCaja />, { wrapper: createQueryWrapper() });

    const resumen = await screen.findByRole('region', { name: 'Resumen de la entrega' });
    expect(within(resumen).getByText('Facturado')).toBeInTheDocument();
    expect(within(resumen).getByText('$3,480.00')).toBeInTheDocument();
    expect(within(resumen).getByText('Apartados')).toBeInTheDocument();
    expect(within(resumen).getByText('TPV')).toBeInTheDocument();

    const facturado = screen.getByRole('region', { name: 'Facturado en la sesión' });
    expect(within(facturado).getByText('Factura F-103')).toBeInTheDocument();
    expect(within(facturado).getByText('Crédito (PPD)')).toBeInTheDocument();

    const notas = screen.getByRole('region', { name: 'Notas de crédito de la sesión' });
    expect(within(notas).getByText('NC-R1')).toBeInTheDocument();
    expect(within(notas).getByText('Ranura')).toBeInTheDocument();

    expect(screen.getByRole('region', { name: 'Movimientos de caja' })).toBeInTheDocument();
  });
});
