import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteBalanzaPage } from './ReporteBalanzaPage';
import { ReporteDiarioPage } from './ReporteDiarioPage';
import { lineasDiario } from '../lib/diario';
import type { PolizaDiario } from '../api/reportes';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const hoy = new Date();
const ANIO = hoy.getFullYear();
const MES = hoy.getMonth() + 1;

function periodo(numero: number) {
  return {
    id: `p${numero}`, ejercicioId: 'e1', anio: ANIO, numero, nombre: String(numero), fechaInicio: `${ANIO}-01-01`,
    fechaFin: `${ANIO}-01-31`, estado: 'Abierto', esAjuste: numero === 13, abiertoPor: null,
  };
}

const COLUMNAS_BALANZA = [
  { clave: 'codigo', etiqueta: 'Cuenta', tipo: 'texto', alineacion: null },
  { clave: 'nombre', etiqueta: 'Nombre', tipo: 'texto', alineacion: null },
  { clave: 'saldoInicial', etiqueta: 'Saldo inicial', tipo: 'moneda', alineacion: 'derecha' },
  { clave: 'cargos', etiqueta: 'Cargos', tipo: 'moneda', alineacion: 'derecha' },
  { clave: 'abonos', etiqueta: 'Abonos', tipo: 'moneda', alineacion: 'derecha' },
  { clave: 'saldoDeudor', etiqueta: 'Saldo deudor', tipo: 'moneda', alineacion: 'derecha' },
  { clave: 'saldoAcreedor', etiqueta: 'Saldo acreedor', tipo: 'moneda', alineacion: 'derecha' },
];

function filaBalanza(codigo: string, nombre: string, inicial: number, cargos: number, abonos: number) {
  const final = inicial + cargos - abonos;
  return {
    cuentaId: `c-${codigo}`, codigo, nombre, nivel: 2, naturaleza: 'Deudora', tieneHijas: false, saldoInicial: inicial, cargos, abonos,
    saldoFinal: final, saldoDeudor: final > 0 ? final : 0, saldoAcreedor: final < 0 ? -final : 0,
  };
}

function balanza(cuadra = true) {
  return {
    titulo: 'Balanza de comprobación',
    generadoEn: '2026-11-03T18:00:00Z',
    filtrosAplicados: { Periodo: `${ANIO}-${MES}`, Tipo: 'Del periodo', Nivel: 'Cuentas con movimiento', Sucursal: 'Todas' },
    columnas: COLUMNAS_BALANZA,
    filas: [filaBalanza('1102', 'Bancos', -1000, 0, 800), filaBalanza('6101', 'Gastos de mantenimiento', 1000, 800, 0)],
    totales: {
      saldoInicialDeudor: 1000, saldoInicialAcreedor: 1000, cargos: 800, abonos: cuadra ? 800 : 799, saldoFinalDeudor: 1800,
      saldoFinalAcreedor: cuadra ? 1800 : 1799, cuadra, cuentas: 2,
    },
    anio: ANIO, desde: MES, hasta: MES, nivel: null, sucursalId: null,
  };
}

function servidorBase(peticiones: string[]) {
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () => HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 })),
    http.get('*/api/v1/contabilidad/periodos/ejercicios', () =>
      HttpResponse.json([{ id: 'e1', anio: ANIO, version: 1, periodos: Array.from({ length: 13 }, (_, i) => periodo(i + 1)) }])),
    http.get('*/api/v1/contabilidad/reportes/*', ({ request }) => {
      peticiones.push(request.url);
      return HttpResponse.json({ code: 'NO_MOCK' }, { status: 500 });
    }),
  );
}

describe('<ReporteBalanzaPage>', () => {
  it('al abrir ejecuta la balanza del mes en curso, muestra las cuentas y el cuadre', async () => {
    const peticiones: string[] = [];
    servidorBase(peticiones);
    mswServer.use(
      http.get('*/api/v1/contabilidad/reportes/balanza', ({ request }) => {
        peticiones.push(request.url);
        return HttpResponse.json(balanza());
      }),
    );
    render(<ReporteBalanzaPage />, { wrapper: createQueryWrapper() });

    const celda = await screen.findByText('Gastos de mantenimiento');
    const fila = within(celda.closest('tr')!);
    expect(fila.getByText('$1,800.00')).toBeInTheDocument();
    expect(screen.getByText('Cuadra')).toBeInTheDocument();
    // El Link simulado no reenvía aria-label: se cuenta un enlace al mayor por cuenta.
    expect(screen.getAllByRole('link', { name: 'Mayor' }).map((l) => l.getAttribute('href'))).toEqual([
      '/contabilidad/reportes/mayor',
      '/contabilidad/reportes/mayor',
    ]);
    const url = new URL(peticiones.find((p) => p.includes('/balanza'))!);
    expect(url.searchParams.get('anio')).toBe(String(ANIO));
    expect(url.searchParams.get('periodo')).toBe(String(MES));
    expect(url.searchParams.get('acumulada')).toBe('false');
  });

  it('con nivel y acumulada los envía al ejecutar, y marca «No cuadra» con su explicación', async () => {
    const peticiones: string[] = [];
    servidorBase(peticiones);
    mswServer.use(
      http.get('*/api/v1/contabilidad/reportes/balanza', ({ request }) => {
        peticiones.push(request.url);
        return HttpResponse.json(balanza(new URL(request.url).searchParams.get('nivel') !== '2'));
      }),
    );
    render(<ReporteBalanzaPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('Gastos de mantenimiento');

    fireEvent.change(screen.getByLabelText('Nivel'), { target: { value: '2' } });
    fireEvent.change(screen.getByLabelText('Tipo'), { target: { value: 'acumulada' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ejecutar' }));

    await waitFor(() => expect(screen.getByText('No cuadra')).toBeInTheDocument());
    const url = new URL(peticiones.filter((p) => p.includes('/balanza')).at(-1)!);
    expect(url.searchParams.get('nivel')).toBe('2');
    expect(url.searchParams.get('acumulada')).toBe('true');
    expect(screen.getByRole('note')).toHaveTextContent('no coinciden');
  });
});

const POLIZAS: PolizaDiario[] = [
  {
    id: 'po1', folio: 'DR-000001', tipoClave: 'DR', fecha: `${ANIO}-01-05`, periodoAnio: ANIO, periodoNumero: MES,
    concepto: 'Provisión de mantenimiento', origen: 'Manual', estado: 'Autorizada', origenModulo: null, origenFolio: null,
    cargos: 800, abonos: 800,
    partidas: [
      { linea: 1, cuentaId: 'g', cuentaCodigo: '6101', cuentaNombre: 'Gastos de mantenimiento', concepto: null, sucursalId: 's1', sucursalNombre: 'Conkal', cargo: 800, abono: 0 },
      { linea: 2, cuentaId: 'b', cuentaCodigo: '1102', cuentaNombre: 'Bancos', concepto: 'Pago', sucursalId: 's1', sucursalNombre: null, cargo: 0, abono: 800 },
    ],
  },
];

describe('lineasDiario', () => {
  it('aplana cada partida con los datos de su póliza y pone textos para lo que falta', () => {
    const lineas = lineasDiario(POLIZAS);
    expect(lineas).toHaveLength(2);
    expect(lineas[0]).toMatchObject({ folio: 'DR-000001', concepto: 'Provisión de mantenimiento', cuentaCodigo: '6101', cargo: 800 });
    expect(lineas[1]).toMatchObject({ concepto: 'Pago', sucursalNombre: 'Sin sucursal', abono: 800 });
  });
});

describe('<ReporteDiarioPage>', () => {
  it('muestra las partidas del periodo, los totales y la paginación cuando hay más pólizas', async () => {
    const peticiones: string[] = [];
    servidorBase(peticiones);
    mswServer.use(
      http.get('*/api/v1/contabilidad/reportes/diario', ({ request }) => {
        peticiones.push(request.url);
        return HttpResponse.json({
          titulo: 'Libro diario', generadoEn: '2026-11-03T18:00:00Z', filtrosAplicados: { Periodo: `${ANIO}-${MES}` },
          polizas: POLIZAS, totales: { polizas: 501, partidas: 1002, cargos: 400800, abonos: 400800, cuadra: true },
          total: 501, offset: 0, limit: 500, anio: ANIO, desde: MES, hasta: MES, sucursalId: null,
        });
      }),
    );
    render(<ReporteDiarioPage />, { wrapper: createQueryWrapper() });

    expect((await screen.findAllByText('Provisión de mantenimiento')).length).toBeGreaterThan(0);
    expect(screen.getByText('Bancos')).toBeInTheDocument();
    expect(screen.getByText('Cuadra')).toBeInTheDocument();
    expect(screen.getByText('1,002')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Páginas del diario' })).toHaveTextContent('Pólizas 1 a 1 de 501');
    expect(screen.getByRole('button', { name: 'Siguiente' })).toBeEnabled();
    expect(new URL(peticiones.find((p) => p.includes('/diario'))!).searchParams.get('limit')).toBe('500');
  });
});
