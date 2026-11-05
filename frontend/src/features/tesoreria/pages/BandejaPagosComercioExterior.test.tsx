import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaPagosComercioExterior } from './BandejaPagosComercioExterior';

const fila = (o: object = {}) => ({
  facturaProveedorId: 'f-1', tipo: 'Importacion', estado: 'PendienteNivel2', nivelPendiente: 2,
  proveedorId: 'p1', proveedorRazonSocial: 'Agencia Aduanal Ejemplo SA de CV', folioProveedor: 'AA-2041',
  saldoPendiente: 48250, moneda: 'MXN', fechaVencimiento: '2026-11-20',
  nivel1UsuarioId: 'u1', nivel1Nombre: 'Compras MP', nivel1En: '2026-11-05T10:00:00Z',
  nivel2UsuarioId: null, nivel2Nombre: null, nivel2En: null, motivoRechazo: null,
  puedoFirmar: true, puedoRechazar: true, ...o,
});

function bandeja(items: object[]) {
  mswServer.use(
    http.get('*/api/v1/tesoreria/pagos-comercio-exterior', () =>
      HttpResponse.json({ items, offset: 0, limit: 100, total: items.length })),
  );
  render(<BandejaPagosComercioExterior />, { wrapper: createQueryWrapper() });
}

describe('<BandejaPagosComercioExterior>', () => {
  it('ofrece el nivel pendiente y manda la Idempotency-Key al autorizar', async () => {
    let visto: Request | null = null;
    mswServer.use(
      http.post('*/api/v1/tesoreria/pagos-comercio-exterior/f-1/autorizar/2', ({ request }) => {
        visto = request;
        return HttpResponse.json(fila({ estado: 'Autorizado', nivelPendiente: null }));
      }),
    );
    bandeja([fila()]);
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar nivel 2' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar' }));
    await waitFor(() => expect(visto).not.toBeNull());
    expect(visto!.headers.get('Idempotency-Key')).toBeTruthy();
  });

  it('sin facultad para el nivel pendiente no ofrece acciones', async () => {
    bandeja([fila({ puedoFirmar: false, puedoRechazar: false })]);
    await screen.findByText('Pendiente de nivel 2');
    expect(screen.queryByRole('button', { name: /Autorizar nivel/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument();
  });
});
