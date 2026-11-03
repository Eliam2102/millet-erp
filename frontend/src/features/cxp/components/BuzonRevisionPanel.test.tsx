import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BuzonRevisionPanel } from '@/features/cxp/components/BuzonRevisionPanel';

const base = '*/api/v1/cuentas-por-pagar/cfdis/buzon';

describe('<BuzonRevisionPanel>', () => {
  it('lista el correo por revisar con su motivo y lo descarta', async () => {
    let descartado = false;
    mswServer.use(
      http.get(`${base}/revision`, () =>
        HttpResponse.json({
          items: [
            {
              id: 'b1',
              asunto: 'Factura de octubre',
              remitente: 'prov@ejemplo.com',
              recibidoEn: '2026-11-02T10:00:00Z',
              motivo: 'SIN_XML',
              registradoEn: '2026-11-02T10:15:00Z',
              descartado: false,
              motivoDescarte: null,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
      http.get(`${base}/ejecuciones`, () =>
        HttpResponse.json([
          {
            id: 'e1',
            inicioUtc: '2026-11-02T10:15:00Z',
            finUtc: '2026-11-02T10:15:02Z',
            mensajes: 1,
            ingresados: 0,
            duplicados: 0,
            aRevision: 1,
            error: null,
          },
        ]),
      ),
      http.post(`${base}/revision/b1/descartar`, () => {
        descartado = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<BuzonRevisionPanel puedeDescartar />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Factura de octubre')).toBeInTheDocument();
    expect(screen.getByText('El correo no trae XML')).toBeInTheDocument();
    expect(screen.getByText(/1 a revisión/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Descartar correo/ }));
    await waitFor(() => expect(descartado).toBe(true));
  });

  it('sin corridas ni correos no pinta nada', async () => {
    mswServer.use(
      http.get(`${base}/revision`, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
      http.get(`${base}/ejecuciones`, () => HttpResponse.json([])),
    );
    const { container } = render(<BuzonRevisionPanel puedeDescartar />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });
});
