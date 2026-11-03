import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteCuentaPuentePage } from './ReporteCuentaPuentePage';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const API = '*/api/v1/contabilidad/reportes/cuenta-puente';
const OC2 = '00000000-0000-0000-0000-0000000000b2';
const OC3 = '00000000-0000-0000-0000-0000000000b3';

const COLUMNAS = [
  { clave: 'ordenCompraFolio', etiqueta: 'Orden de compra', tipo: 'texto', alineacion: null },
  { clave: 'proveedorNombre', etiqueta: 'Proveedor', tipo: 'texto', alineacion: null },
  { clave: 'sucursalNombre', etiqueta: 'Sucursal', tipo: 'texto', alineacion: null },
  { clave: 'cargos', etiqueta: 'Facturado (cargos)', tipo: 'moneda', alineacion: 'derecha' },
  { clave: 'abonos', etiqueta: 'Recibido (abonos)', tipo: 'moneda', alineacion: 'derecha' },
  { clave: 'saldo', etiqueta: 'Saldo', tipo: 'moneda', alineacion: 'derecha' },
  { clave: 'naturalezaEtiqueta', etiqueta: 'Naturaleza', tipo: 'texto', alineacion: null },
  { clave: 'fechaPrimerMovimientoSinSaldar', etiqueta: 'Desde', tipo: 'fecha', alineacion: null },
  { clave: 'diasAntiguedad', etiqueta: 'Días', tipo: 'numero', alineacion: 'derecha' },
  { clave: 'tramoEtiqueta', etiqueta: 'Antigüedad', tipo: 'texto', alineacion: null },
  { clave: 'diferenciaCambiaria', etiqueta: 'Diferencia cambiaria (informativa)', tipo: 'moneda', alineacion: 'derecha' },
];

function fila(o: Record<string, unknown>) {
  return {
    proveedorId: 'p1', proveedorClave: 'DEMO-PROV-ACT', proveedorNombre: 'FIX Suministros', sucursalIds: ['s1'],
    sucursalNombre: 'FIX Conkal', diferenciaCambiaria: null, ...o,
  };
}

const FILAS = [
  fila({
    ordenCompraId: OC3, ordenCompraFolio: 'OC-DEMO-K105-03', cargos: 0, abonos: 3000, saldo: -3000,
    naturaleza: 'RecibidoSinFacturar', naturalezaEtiqueta: 'Recibido sin facturar', fechaPrimerMovimientoSinSaldar: '2026-08-26',
    diasAntiguedad: 61, tramo: 'MasDe60', tramoEtiqueta: 'Más de 60', ultimoMovimiento: '2026-08-26',
  }),
  fila({
    ordenCompraId: OC2, ordenCompraFolio: 'OC-DEMO-K105-02', cargos: 5000, abonos: 0, saldo: 5000,
    naturaleza: 'FacturadoSinRecibir', naturalezaEtiqueta: 'Facturado sin recibir', fechaPrimerMovimientoSinSaldar: '2026-10-10',
    diasAntiguedad: 16, tramo: 'De0a30', tramoEtiqueta: '0–30', ultimoMovimiento: '2026-10-10',
  }),
];

function reporte(totales: Record<string, unknown> = {}) {
  return {
    titulo: 'Conciliación de la cuenta puente recepción–factura',
    generadoEn: '2026-10-26T18:00:00Z',
    filtrosAplicados: { Corte: '2026-10-26', 'Cuenta puente': 'DEMO-PUENTE DEMO Cuenta puente', Sucursal: 'Todas', Proveedor: 'Todos', 'Solo con saldo': 'Sí' },
    columnas: COLUMNAS,
    filas: FILAS,
    totales: {
      totalReporte: 2000, saldoBalanza: 2000, partidasSinOc: 0, cuadra: true, ordenesConSaldo: 2,
      facturadoSinRecibir: 5000, recibidoSinFacturar: -3000, tramo0a30: 5000, tramo31a60: 0, tramoMasDe60: -3000,
      ...totales,
    },
    corte: '2026-10-26',
    cuentaPuente: { id: 'c1', codigo: 'DEMO-PUENTE', nombre: 'DEMO Cuenta puente recepción–factura' },
  };
}

function servidor(datos = reporte(), peticiones: string[] = []) {
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () => HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 })),
    http.get('*/api/v1/catalogos/proveedores', () => HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 })),
    http.get(API, ({ request }) => {
      peticiones.push(request.url);
      return HttpResponse.json(datos);
    }),
    http.get(`${API}/:ocId/movimientos`, ({ params }) =>
      HttpResponse.json({
        ordenCompraId: params.ocId, ordenCompraFolio: 'OC-DEMO-K105-03', proveedorId: 'p1', proveedorNombre: 'FIX Suministros',
        corte: '2026-10-26', cuentaPuente: datos.cuentaPuente, saldo: FILAS[0],
        movimientos: [
          {
            id: 'm1', polizaId: 'po1', polizaFolio: 'EM-000017', fecha: '2026-08-26', tipoDocumento: 'Recepcion', documentoId: 'd1',
            facturaOrigenId: null, lineaOcId: null, sucursalId: 's1', cargo: 0, abono: 3000, saldoAcumulado: -3000,
            moneda: 'MXN', tipoCambio: 1, importeOriginal: null, esReversa: false, movimientoRevertidoId: null,
          },
        ],
      })),
  );
}

async function filaDe(folio: string) {
  const celda = await screen.findByText(folio);
  return within(celda.closest('tr')!);
}

describe('<ReporteCuentaPuentePage>', () => {
  it('ejecuta el reporte al abrir: muestra las OC con saldo, su naturaleza y el tramo «Más de 60»', async () => {
    const peticiones: string[] = [];
    servidor(reporte(), peticiones);
    render(<ReporteCuentaPuentePage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByRole('heading', { name: 'Conciliación de la cuenta puente recepción–factura' })).toBeInTheDocument();
    const oc3 = await filaDe('OC-DEMO-K105-03');
    expect(oc3.getByText('Más de 60')).toBeInTheDocument();
    expect(oc3.getByText('Recibido sin facturar')).toBeInTheDocument();
    expect(oc3.getByText('61')).toBeInTheDocument();
    const oc2 = await filaDe('OC-DEMO-K105-02');
    expect(oc2.getByText('Facturado sin recibir')).toBeInTheDocument();
    expect(oc2.getByText('FIX Suministros')).toBeInTheDocument();
    expect(peticiones[0]).toContain('soloConSaldo=true');
    expect(peticiones[0]).toMatch(/corte=\d{4}-\d{2}-\d{2}/);
  });

  it('tarjeta de cuadre: total del reporte contra el saldo en balanza; «Cuadra» cuando no hay partidas sin OC', async () => {
    servidor();
    render(<ReporteCuentaPuentePage />, { wrapper: createQueryWrapper() });
    const tarjeta = within(await screen.findByRole('region', { name: 'Cuadre contra balanza' }));
    expect(tarjeta.getByText('Cuadra')).toBeInTheDocument();
    expect(tarjeta.getByText('Total del reporte').nextSibling).toHaveTextContent('$2,000.00');
    expect(tarjeta.getByText('Saldo en balanza').nextSibling).toHaveTextContent('$2,000.00');
    expect(tarjeta.getByText('Partidas sin OC').nextSibling).toHaveTextContent('$0.00');
    expect(tarjeta.queryByRole('note')).not.toBeInTheDocument();
  });

  it('tarjeta de cuadre: «No cuadra» con aviso de las partidas sin OC', async () => {
    servidor(reporte({ saldoBalanza: 2500, partidasSinOc: 500, cuadra: false }));
    render(<ReporteCuentaPuentePage />, { wrapper: createQueryWrapper() });
    const region = await screen.findByRole('region', { name: 'Cuadre contra balanza' });
    const tarjeta = within(region);
    expect(tarjeta.getByText('No cuadra')).toBeInTheDocument();
    expect(region).toHaveClass('ring-danger-ring');
    expect(tarjeta.getByRole('note')).toHaveTextContent('$500.00 en partidas sin orden de compra');
  });

  it('con filtro de proveedor no concilia contra la balanza', async () => {
    servidor(reporte({ saldoBalanza: null, partidasSinOc: null, cuadra: null }));
    render(<ReporteCuentaPuentePage />, { wrapper: createQueryWrapper() });
    const tarjeta = within(await screen.findByRole('region', { name: 'Cuadre contra balanza' }));
    expect(tarjeta.queryByText('Cuadra')).not.toBeInTheDocument();
    expect(tarjeta.queryByText('No cuadra')).not.toBeInTheDocument();
    expect(tarjeta.getByRole('note')).toHaveTextContent('Con filtro de proveedor no se concilia contra la balanza');
  });

  it('abre el detalle de movimientos de la OC con póliza, documento y saldo acumulado', async () => {
    servidor();
    render(<ReporteCuentaPuentePage />, { wrapper: createQueryWrapper() });
    const boton = (await filaDe('OC-DEMO-K105-03')).getByRole('button', { name: 'Ver movimientos de OC-DEMO-K105-03' });
    fireEvent.click(boton);
    const dialogo = await screen.findByRole('dialog', { name: 'Movimientos de la cuenta puente' });
    const tabla = within(await within(dialogo).findByRole('table', { name: 'Movimientos de la orden de compra' }));
    const renglon = within(tabla.getByText('EM-000017').closest('tr')!);
    expect(renglon.getByText('Recepción')).toBeInTheDocument();
    expect(renglon.getByText('$3,000.00')).toBeInTheDocument();
    expect(renglon.getByText('-$3,000.00')).toBeInTheDocument();
    expect(within(dialogo).getByText('Recibido sin facturar')).toBeInTheDocument();
    fireEvent.keyDown(dialogo, { key: 'Escape', code: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(boton).toHaveFocus();
  });
});
