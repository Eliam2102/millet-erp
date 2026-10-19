import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CorridaAwDetalle } from '@/modules/datos-maestros/components/CorridaAwDetalle';

const descargarReporteExcel = vi.fn();
vi.mock('@/components/erp/reportes/exporters/reporte-excel-exporter', () => ({
  descargarReporteExcel: (...args: unknown[]) => descargarReporteExcel(...args),
}));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => vi.fn(),
}));

const ID = '0190aaaa-0000-7000-8000-000000000001';
const reporte = {
  titulo: 'Diferencias de la corrida del 17/10/2026 10:00 UTC', generadoEn: '2026-10-17T10:05:00Z',
  filtrosAplicados: { Corrida: ID }, columnas: [{ clave: 'entidad', etiqueta: 'Entidad', tipo: 'texto' }],
  filas: [{ entidad: 'ProductoAw' }], totales: { productosLeidos: 3 }, truncado: false,
};

function ejecucion(resumenProductos: string | null) {
  return {
    ejecucion: {
      id: ID, tipo: 'Barrido', estado: 'Completa', leidos: 10, creados: 4, actualizados: 2, sinCambios: 3, pendientes: 0,
      conflictos: 1, errores: 1, iniciadaEnUtc: '2026-10-17T10:00:00Z', terminadaEnUtc: '2026-10-17T10:01:00Z', actor: 'a@b.com',
      reintentoDeId: null, errorGeneral: null, resumenProductos,
    },
    errores: [], erroresTruncados: false,
  };
}

function arrancar(resumenProductos: string | null) {
  mswServer.use(
    http.get('*/clientes/sincronizacion/ejecuciones/*', () => HttpResponse.json(ejecucion(resumenProductos))),
    http.get('*/integraciones/aw/origen', () => HttpResponse.json({
      origen: 'Real', permitido: false, demoConfigurada: false, origenClientesReal: 'Real', origenProductosReal: 'Real',
      cambiadoPor: null, cambiadoEn: null, version: 1,
    })),
    http.get('*/conflictos-aw', () => HttpResponse.json([])),
    http.get(`*/sincronizacion-aw/corridas/${ID}/diferencias`, () => HttpResponse.json(reporte)),
  );
  render(<CorridaAwDetalle id={ID} />, { wrapper: createQueryWrapper() });
}

describe('<CorridaAwDetalle>', () => {
  beforeEach(() => {
    descargarReporteExcel.mockReset();
    useAuthStore.setState({
      status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u', email: 'a@b.com', nombre: 'A' }, empresas: [], currentEmpresaId: 'e', errorMessage: null,
      permisos: [PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosProductosAwGestionar],
    });
  });

  it('muestra los KPI de clientes y de productos a partir del resumen JSON', async () => {
    arrancar(JSON.stringify({ leidos: 7, creados: 5, actualizados: 1, sinCambios: 1, errores: 0 }));
    const productos = await screen.findByRole('region', { name: 'Productos' });
    await waitFor(() => expect(productos).toHaveTextContent('7'));
    expect(productos).toHaveTextContent('6'); // creados + actualizados
    expect(screen.getByRole('region', { name: 'Clientes' })).toHaveTextContent('10');
  });

  it('«Descargar reporte de diferencias» entrega el JSON del endpoint al exportador', async () => {
    arrancar(JSON.stringify({ leidos: 1 }));
    fireEvent.click(await screen.findByRole('button', { name: 'Descargar reporte de diferencias' }));
    await waitFor(() => expect(descargarReporteExcel).toHaveBeenCalledTimes(1));
    const [enviado, nombre] = descargarReporteExcel.mock.calls[0] as [typeof reporte, string];
    expect(enviado.titulo).toBe(reporte.titulo);
    expect(enviado.filas).toEqual(reporte.filas);
    expect(nombre).toBe('diferencias-aw-0190aaaa.xlsx');
  });

  it('un resumen de productos con error se anuncia con role alert', async () => {
    arrancar(JSON.stringify({ error: 'origen_deshabilitado', mensaje: 'La lectura de productos A+W está deshabilitada.' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('La lectura de productos A+W está deshabilitada.');
  });
});
