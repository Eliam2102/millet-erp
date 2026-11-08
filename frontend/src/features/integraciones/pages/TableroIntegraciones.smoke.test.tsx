import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TableroIntegraciones } from '@/features/integraciones/pages/TableroIntegraciones';
import type {
  AlertaIntegracion,
  FuenteIntegracion,
  TableroIntegraciones as Tablero,
} from '@/features/integraciones/api/useTableroIntegraciones';

const BASE = '*/api/v1/integraciones/tablero';

const outbox: FuenteIntegracion = {
  clave: 'outbox.compras',
  nombre: 'Eventos de Compras (compras-events)',
  grupo: 'Service Bus',
  permiteReintento: true,
  disponible: true,
  errorLectura: null,
  pendientes: 0,
  reintentando: 0,
  enError: 1,
  okUltimas24h: 40,
  ultimoExito: '2026-11-09T14:59:00Z',
  pendienteMasAntiguo: null,
  detenido: false,
  mensajes: [
    {
      id: 'm-1',
      referencia: 'compras.orden-compra.autorizada.v1',
      estado: 3,
      intentos: 11,
      ultimoError: 'Timeout al publicar',
      creadoEn: '2026-11-09T14:00:00Z',
      procesadoEn: null,
    },
    {
      id: 'm-2',
      referencia: 'compras.orden-compra.cerrada.v1',
      estado: 4,
      intentos: 0,
      ultimoError: null,
      creadoEn: '2026-11-09T14:10:00Z',
      procesadoEn: '2026-11-09T14:10:05Z',
    },
  ],
};

const dlq: FuenteIntegracion = {
  ...outbox,
  clave: 'service-bus.dead-letter',
  nombre: 'Cola de errores de Service Bus (dead-letter)',
  permiteReintento: false,
  enError: 0,
  okUltimas24h: 0,
  mensajes: [],
};

const alerta: AlertaIntegracion = {
  id: 'a-1',
  fuenteClave: 'outbox.compras',
  fuenteNombre: 'Eventos de Compras (compras-events)',
  tipo: 1,
  detalle: '1 mensaje(s) agotaron sus reintentos y esperan reintento manual.',
  abiertaEn: '2026-11-09T14:05:00Z',
  notificadaEn: '2026-11-09T14:05:00Z',
  cerradaEn: null,
  recuperacionNotificadaEn: null,
};

const tablero: Tablero = {
  generadoEn: '2026-11-09T15:00:00Z',
  umbralDetenidoMinutos: 15,
  fuentes: [outbox, dlq],
  alertasAbiertas: [alerta],
};

describe('<TableroIntegraciones> — smoke (I13.6)', () => {
  it('muestra la alerta, el estado de cada integración y reintenta a mano un mensaje en error', async () => {
    let reintentado: string | null = null;
    mswServer.use(
      http.get(BASE, () => HttpResponse.json(tablero)),
      http.get(`${BASE}/alertas`, () =>
        HttpResponse.json([
          alerta,
          { ...alerta, id: 'a-0', tipo: 2, cerradaEn: '2026-11-09T10:00:00Z' },
        ]),
      ),
      http.post(`${BASE}/:clave/mensajes/:id/reintentar`, ({ params }) => {
        reintentado = `${String(params.clave)}/${String(params.id)}`;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<TableroIntegraciones />, { wrapper: createQueryWrapper() });

    const aviso = await screen.findByTestId('alerta-a-1');
    expect(aviso).toHaveTextContent('Eventos de Compras (compras-events) · Mensajes en error');
    expect(
      within(screen.getByTestId('fuente-outbox.compras')).getByText('Con errores'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId('fuente-service-bus.dead-letter')).getByText('Al día'),
    ).toBeInTheDocument();

    const enError = screen.getByTestId('mensaje-m-1');
    expect(within(enError).getByText('Timeout al publicar')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('mensaje-m-2')).queryByRole('button', { name: 'Reintentar' }),
    ).not.toBeInTheDocument();
    fireEvent.click(within(enError).getByRole('button', { name: 'Reintentar' }));
    await waitFor(() => expect(reintentado).toBe('outbox.compras/m-1'));

    expect(
      within(await screen.findByTestId('historial-a-0')).getByText('Recuperada'),
    ).toBeInTheDocument();
  });

  it('una integración de solo lectura no ofrece reintento', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json({
          ...tablero,
          alertasAbiertas: [],
          fuentes: [{ ...dlq, enError: 1, mensajes: [{ ...outbox.mensajes[0], id: 'd-1' }] }],
        }),
      ),
      http.get(`${BASE}/alertas`, () => HttpResponse.json([])),
    );

    render(<TableroIntegraciones />, { wrapper: createQueryWrapper() });

    const fila = await screen.findByTestId('mensaje-d-1');
    expect(within(fila).queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
    expect(screen.getByText(/no se reintenta desde aquí/)).toBeInTheDocument();
    expect(screen.getByText('Sin alertas registradas.')).toBeInTheDocument();
  });
});
