import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaContingencias } from '@/features/facturacion/pages/BandejaContingencias';
import type { ContingenciaItem } from '@/features/facturacion/api/useContingencias';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));

vi.mock('@/components/erp', async (original) => ({
  ...(await original<typeof import('@/components/erp')>()),
  SucursalSelector: ({ onChange }: { onChange: (id: string | null) => void }) => (
    <button type="button" onClick={() => onChange('suc-1')}>
      Elegir sucursal
    </button>
  ),
}));

const base: ContingenciaItem = {
  id: 'c-1',
  sucursalId: 'suc-1',
  tipo: 'Pac',
  origen: 'Automatica',
  estado: 'Abierta',
  motivo: 'ServiceUnavailable: PAC sin servicio',
  errorCodigo: 'ServiceUnavailable',
  ocurridaAt: '2026-11-28T16:00:00Z',
  fechaLimite: '2026-12-03',
  porVencer: true,
  vencida: false,
  comprobanteId: 'f-1',
  comprobanteFolio: 'FV-000101',
  comprobanteEstado: 'TimbradoFallido',
  registradaPor: 'Ingresos demo',
  cerradaAt: null,
  uuidFinal: null,
  notaCierre: null,
  cerradaPor: null,
  version: 1,
};

const reporteVacio = {
  titulo: 'Bitácora de contingencias de timbrado',
  generadoEn: '2026-12-02T15:00:00Z',
  filtrosAplicados: { Mes: '11/2026' },
  columnas: [{ clave: 'tipo', etiqueta: 'Tipo', tipo: 'texto' }],
  filas: [],
  totales: { Contingencias: 0 },
};

describe('<BandejaContingencias> — smoke (F6.8)', () => {
  it('avisa las abiertas por regularizar y muestra el CFDI final de una regularizada', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/contingencias-timbrado/reporte', () => HttpResponse.json(reporteVacio)),
      http.get('*/api/v1/facturacion/contingencias-timbrado', () =>
        HttpResponse.json({
          items: [
            base,
            {
              ...base,
              id: 'c-2',
              estado: 'Regularizada',
              porVencer: false,
              comprobanteId: 'f-2',
              comprobanteFolio: 'FV-000102',
              comprobanteEstado: 'Timbrado',
              cerradaAt: '2026-11-29T16:00:00Z',
              uuidFinal: 'AAAAAAAA-0000-4000-8000-000000000001',
            },
          ],
          abiertas: 1,
          porVencer: 1,
          vencidas: 0,
        }),
      ),
    );

    render(<BandejaContingencias />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('FV-000101')).toBeInTheDocument());
    const alerta = screen.getByTestId('alerta-contingencias');
    expect(within(alerta).getByRole('note')).toHaveTextContent('1 contingencia(s) por regularizar');

    const regularizada = screen.getByTestId('contingencia-c-2');
    expect(within(regularizada).getByText('AAAAAAAA-0000-4000-8000-000000000001')).toBeInTheDocument();
    expect(within(regularizada).getByText('Regularizada')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('contingencia-c-1')).getByText(
        'Se regulariza al timbrar o descartar el documento.',
      ),
    ).toBeInTheDocument();
  });

  it('registra a mano una contingencia del SAT y explica qué falta', async () => {
    let cuerpo: Record<string, unknown> | null = null;
    mswServer.use(
      http.get('*/api/v1/facturacion/contingencias-timbrado/reporte', () => HttpResponse.json(reporteVacio)),
      http.get('*/api/v1/facturacion/contingencias-timbrado', () =>
        HttpResponse.json({ items: [], abiertas: 0, porVencer: 0, vencidas: 0 }),
      ),
      http.post('*/api/v1/facturacion/contingencias-timbrado', async ({ request }) => {
        cuerpo = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ ...base, id: 'c-3', tipo: 'Sat', origen: 'Manual' }, { status: 201 });
      }),
    );

    render(<BandejaContingencias />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('No hay contingencias en este mes y estado.')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('button', { name: 'Registrar contingencia' }));
    const registrar = screen.getByRole('button', { name: 'Registrar' });
    expect(registrar).toBeDisabled();
    expect(screen.getByText('Elige la sucursal.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Elegir sucursal' }));
    fireEvent.change(screen.getByLabelText('Ocurrió'), { target: { value: '2026-11-28T09:00' } });
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'SAT sin servicio' } });
    expect(registrar).toBeEnabled();
    fireEvent.click(registrar);

    await waitFor(() => expect(cuerpo).not.toBeNull());
    expect(cuerpo).toMatchObject({ sucursalId: 'suc-1', tipo: 'Sat', motivo: 'SAT sin servicio' });
  });

  it('M-285: lista las facturas timbradas con efectos pendientes y las reintenta', async () => {
    let reintentos = 0;
    mswServer.use(
      http.get('*/api/v1/facturacion/contingencias-timbrado/reporte', () => HttpResponse.json(reporteVacio)),
      http.get('*/api/v1/facturacion/contingencias-timbrado', () =>
        HttpResponse.json({ items: [], vencidas: 0, porVencer: 0 }),
      ),
      http.get('*/api/v1/facturacion/comprobantes/efectos-pendientes', () =>
        HttpResponse.json([
          {
            comprobanteId: 'f-9',
            folio: 'FV-000109',
            uuid: 'UUID-9',
            fechaTimbrado: '2026-11-28T16:00:00Z',
            pendientes: [
              {
                id: 'e-1', tipo: 'EventoContable', descripcion: 'Póliza y cartera (evento de timbrado)',
                estado: 'Pendiente', intentos: 1, ultimoErrorCodigo: 'X', ultimoError: 'Falla simulada',
                ultimoIntentoAt: null, aplicadoAt: null,
              },
            ],
          },
        ]),
      ),
      http.post('*/api/v1/facturacion/comprobantes/f-9/reintentar-efectos', () => {
        reintentos++;
        return HttpResponse.json([]);
      }),
    );

    render(<BandejaContingencias />, { wrapper: createQueryWrapper() });

    const aviso = await screen.findByTestId('efectos-pendientes');
    expect(within(aviso).getByText('FV-000109')).toBeTruthy();
    expect(within(aviso).getByText('Póliza y cartera (evento de timbrado)')).toBeTruthy();
    fireEvent.click(within(aviso).getByRole('button', { name: 'Reintentar pendientes' }));
    await waitFor(() => expect(reintentos).toBe(1));
  });
});
