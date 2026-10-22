import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

function ejecucion(resumenProductos: string | null, errores: unknown[] = []) {
  return {
    ejecucion: {
      id: ID, tipo: 'Barrido', estado: 'Completa', leidos: 10, creados: 4, actualizados: 2, sinCambios: 3, pendientes: 0,
      conflictos: 1, errores: 1, iniciadaEnUtc: '2026-10-17T10:00:00Z', terminadaEnUtc: '2026-10-17T10:01:00Z', actor: 'a@b.com',
      reintentoDeId: null, errorGeneral: null, resumenProductos,
    },
    errores, erroresTruncados: false,
  };
}

function arrancar(resumenProductos: string | null, conflictos: unknown[] = [], errores: unknown[] = []) {
  mswServer.use(
    http.get('*/clientes/sincronizacion/ejecuciones/*', () => HttpResponse.json(ejecucion(resumenProductos, errores))),
    http.get('*/integraciones/aw/origen', () => HttpResponse.json({
      origen: 'Real', permitido: false, demoConfigurada: false, origenClientesReal: 'Real', origenProductosReal: 'Real',
      cambiadoPor: null, cambiadoEn: null, version: 1,
    })),
    http.get('*/conflictos-aw', () => HttpResponse.json(conflictos)),
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

  it('el RFC repetido entre clientes de A+W se muestra como Informativo, no como Resuelto', async () => {
    arrancar(null, [{
      id: 'c1', tipoEntidad: 'Cliente', entidadId: null, referenciaExterna: '62', campo: 'RFC repetido entre clientes de A+W',
      valorAntes: 'AW-61', valorDespues: 'DEMO010101AB1', dueno: 'AW', tipo: 'RfcCompartido', estado: 'Informativo',
      causa: 'Dos clientes de A+W con la misma clave fiscal.', versionEvento: 'h', versionMatriz: '0-propuesta',
      ejecucionId: ID, ocurridoEnUtc: '2026-10-17T10:00:00Z', resolucion: null, version: 1,
    }]);
    const tabla = await screen.findByRole('tabpanel', { name: /^Conflictos/ });
    expect((await within(tabla).findAllByText('Informativo')).length).toBeGreaterThan(0);
    expect(within(tabla).queryByText('Resuelto')).not.toBeInTheDocument();
  });

  it('errores y conflictos van en pestañas; el error muestra cómo llegó de A+W y qué falta', async () => {
    useAuthStore.setState({
      permisos: [PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosClientesOrigenVer],
    });
    mswServer.use(
      http.get('*/clientes/sincronizacion/origen/6682', () => HttpResponse.json({
        referencia: '6682',
        campos: [{ campo: 'NAME1', etiqueta: 'Nombre o razón social', valor: null }, { campo: 'UST_ID', etiqueta: 'RFC', valor: 'ABC010101AB1' }],
        faltantes: [{ campo: 'NAME1', etiqueta: 'Nombre o razón social', regla: 'Obligatorio, no puede ir vacío.', comoDebeQuedar: 'Razón social.' }],
      })),
    );
    arrancar(JSON.stringify({ leidos: 1 }), [], [
      { referencia: '6682', codigo: 'fila_invalida', mensaje: 'NAME1 vacío (referencia 6682).', ocurridoEnUtc: '2026-10-17T10:00:00Z' },
      { referencia: '77', codigo: 'conflicto_correlacion', mensaje: 'Coincide RFC.', ocurridoEnUtc: '2026-10-17T10:00:00Z' },
    ]);
    expect(await screen.findByRole('tab', { name: 'Errores (1)' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Conflictos (1)' })).toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: /ver cómo llegó de a\+w/i }));
    expect(await screen.findByText('ABC010101AB1')).toBeInTheDocument();
    expect(screen.getByText('(vacío) · falta')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copiar solicitud para A+W' })).toBeInTheDocument();
  });

  it('un resumen de productos con error se anuncia con role alert', async () => {
    arrancar(JSON.stringify({ error: 'origen_deshabilitado', mensaje: 'La lectura de productos A+W está deshabilitada.' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('La lectura de productos A+W está deshabilitada.');
  });
});
