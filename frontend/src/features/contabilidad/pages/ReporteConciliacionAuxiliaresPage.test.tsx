import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteConciliacionAuxiliaresPage } from './ReporteConciliacionAuxiliaresPage';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const API = '*/api/v1/contabilidad/reportes/conciliacion-auxiliares';

function control(o: Record<string, unknown>) {
  return {
    saldoContable: null, saldoAuxiliar: null, diferencia: null, cuentas: [], rolFaltante: null, rechazosPendientes: 0,
    notas: [], diferenciasPorDocumento: 0, enlaceDetalleExterno: null, ...o,
  };
}

const RESUMEN = {
  corte: '2026-01-31',
  sucursalId: null,
  generadoEn: '2026-02-01T18:00:00Z',
  todoCuadra: false,
  controles: [
    control({
      control: 'Clientes', etiqueta: 'Clientes', estado: 'SinCuenta', estadoEtiqueta: 'Sin cuenta configurada',
      rolFaltante: 'CLIENTES', rechazosPendientes: 3, enlaceDetalleExterno: '/cxc/conciliacion-polizas',
      notas: ['Asigna una cuenta al rol CLIENTES en Contabilidad › Motor › Roles, o marca la cuenta de control en el catálogo.'],
    }),
    control({
      control: 'Proveedores', etiqueta: 'Proveedores', estado: 'Diferencia', estadoEtiqueta: 'Diferencia',
      saldoContable: 1160, saldoAuxiliar: 1740, diferencia: -580, diferenciasPorDocumento: 2,
      cuentas: [{ id: 'c1', codigo: 'DEMO-201', nombre: 'DEMO Proveedores nacionales' }],
    }),
    control({
      control: 'Bancos', etiqueta: 'Bancos', estado: 'Cuadra', estadoEtiqueta: 'Cuadra',
      saldoContable: 5000, saldoAuxiliar: 5000, diferencia: 0,
    }),
  ],
};

const DETALLE = {
  titulo: 'Conciliación de auxiliares · Proveedores',
  generadoEn: '2026-02-01T18:00:00Z',
  filtrosAplicados: { corte: '2026-01-31', sucursalId: null, control: 'Proveedores' },
  columnas: [
    { clave: 'tipoEtiqueta', etiqueta: 'Diferencia', tipo: 'texto', alineacion: null },
    { clave: 'terceroNombre', etiqueta: 'Proveedor / banco', tipo: 'texto', alineacion: null },
    { clave: 'folio', etiqueta: 'Documento', tipo: 'texto', alineacion: null },
    { clave: 'diferencia', etiqueta: 'Diferencia', tipo: 'moneda', alineacion: 'derecha' },
    { clave: 'causa', etiqueta: 'Causa probable', tipo: 'texto', alineacion: null },
  ],
  filas: [
    {
      tipo: 'DocumentoSinPoliza', tipoEtiqueta: 'Documento sin póliza', terceroId: 'p2', terceroClave: 'DEMO-PRV-2',
      terceroNombre: 'DEMO Vidrios del Sureste', documentoId: 'f2', folio: 'FIX-B-7', uuid: null, moneda: 'MXN',
      saldoAuxiliar: 580, saldoContable: 0, diferencia: -580,
      causa: 'El evento no generó póliza: RolSinCuenta · El rol PROVEEDORES no tiene cuenta',
    },
  ],
  totales: RESUMEN.controles[1],
};

function servidor(peticiones: string[] = []) {
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () => HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 })),
    http.get(API, ({ request }) => {
      peticiones.push(request.url);
      return HttpResponse.json(RESUMEN);
    }),
    http.get(`${API}/:control`, ({ request, params }) => {
      peticiones.push(request.url);
      return params.control === 'proveedores'
        ? HttpResponse.json(DETALLE)
        : HttpResponse.json({ ...DETALLE, titulo: 'Conciliación de auxiliares · Bancos', filas: [], totales: RESUMEN.controles[2] });
    }),
  );
}

describe('ReporteConciliacionAuxiliaresPage (CON-09)', () => {
  it('muestra el estado de cada control y nunca «Cuadra» para el que no tiene cuenta', async () => {
    servidor();
    render(<ReporteConciliacionAuxiliaresPage />, { wrapper: createQueryWrapper() });

    const clientes = await screen.findByRole('article', { name: 'Clientes' });
    expect(within(clientes).getByText('Sin cuenta configurada')).toBeInTheDocument();
    expect(within(clientes).getByText(/Falta la cuenta del rol CLIENTES/)).toBeInTheDocument();
    expect(within(clientes).getByText(/3 evento\(s\) rechazado\(s\)/)).toBeInTheDocument();
    expect(within(clientes).queryByText('Cuadra')).not.toBeInTheDocument();
    // El detalle de clientes es de CxC: solo se enlaza.
    expect(within(clientes).getByRole('link', { name: /Conciliación con pólizas/ })).toHaveAttribute('href', '/cxc/conciliacion-polizas');

    const proveedores = screen.getByRole('article', { name: 'Proveedores' });
    // «Diferencia» es el estado (badge) y también la etiqueta del dato.
    expect(within(proveedores).getAllByText('Diferencia')).toHaveLength(2);
    expect(within(proveedores).getByText('DEMO-201 DEMO Proveedores nacionales')).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Bancos' })).getByText('Cuadra')).toBeInTheDocument();
  });

  it('CON-09-a: lista la factura de proveedor sin póliza con su causa', async () => {
    servidor();
    render(<ReporteConciliacionAuxiliaresPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('FIX-B-7')).toBeInTheDocument();
    expect(screen.getByText('Documento sin póliza')).toBeInTheDocument();
    expect(screen.getByText(/RolSinCuenta · El rol PROVEEDORES no tiene cuenta/)).toBeInTheDocument();
    expect(screen.getByText('DEMO Vidrios del Sureste')).toBeInTheDocument();
  });

  it('cambia el detalle a bancos y ejecuta con el corte capturado', async () => {
    const peticiones: string[] = [];
    servidor(peticiones);
    render(<ReporteConciliacionAuxiliaresPage />, { wrapper: createQueryWrapper() });

    const bancos = await screen.findByRole('article', { name: 'Bancos' });
    fireEvent.click(within(bancos).getByRole('button', { name: /Ver diferencias/ }));
    await waitFor(() => expect(peticiones.some((u) => u.includes('/conciliacion-auxiliares/bancos'))).toBe(true));

    fireEvent.change(screen.getByLabelText('Corte *'), { target: { value: '2026-01-31' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ejecutar' }));
    await waitFor(() => expect(peticiones.some((u) => u.includes('corte=2026-01-31'))).toBe(true));
  });
});
