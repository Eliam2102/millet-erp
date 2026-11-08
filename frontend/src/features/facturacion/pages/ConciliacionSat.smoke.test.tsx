import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ConciliacionSat } from '@/features/facturacion/pages/ConciliacionSat';
import type {
  ConciliacionSat as Corrida,
  DiferenciaSat,
} from '@/features/facturacion/api/useConciliacionSat';

/** Margen de las esperas asíncronas de la prueba que sube el archivo (ms). */
const ESPERA = 10_000;

const BASE = '*/api/v1/facturacion/conciliacion-sat';

const corrida: Corrida = {
  id: 'r-1',
  desde: '2026-11-02',
  hasta: '2026-11-08',
  fuente: 1,
  nombreArchivo: 'emitidos-semana.txt',
  ejecutadaEn: '2026-11-09T15:00:00Z',
  ejecutadaPor: 'u-1',
  cfdiSat: 12,
  cfdiErp: 12,
  coincidentes: 10,
  conDiferencias: 2,
  sinDiferencias: false,
  diferenciasPendientes: 2,
  porTipo: [
    { tipo: 1, total: 1, revisadas: 0 },
    { tipo: 3, total: 1, revisadas: 0 },
  ],
};

const cancelada: DiferenciaSat = {
  id: 'd-1',
  uuid: 'AAAAAAAA-0000-4000-8000-000000000001',
  tipo: 3,
  comprobanteId: 'c-1',
  folio: 'FV-000101',
  rfcReceptor: 'BBB010101BBB',
  valorSat: 'Cancelado',
  valorErp: 'Vigente',
  revisada: false,
  revisadaPor: null,
  revisadaEn: null,
  nota: null,
  revisadaPorNombre: null,
  moneda: 'MXN',
};

describe('<ConciliacionSat> — smoke (FAC-10)', () => {
  it('muestra la última corrida con sus diferencias y marca una como revisada con su nota', async () => {
    let cuerpo: Record<string, unknown> | null = null;
    mswServer.use(
      http.get(BASE, () => HttpResponse.json([corrida])),
      http.get(`${BASE}/r-1/diferencias`, () =>
        HttpResponse.json([
          cancelada,
          {
            ...cancelada,
            id: 'd-2',
            tipo: 1,
            comprobanteId: null,
            folio: null,
            valorSat: 'Vigente · 5,000.00 · I',
            valorErp: null,
            moneda: null,
          },
          {
            ...cancelada,
            id: 'd-3',
            tipo: 4,
            valorSat: '10,500.00',
            valorErp: '10,000.00',
            moneda: 'USD',
            revisada: true,
            revisadaPorNombre: 'Ana Pruebas',
            revisadaEn: '2026-11-09T15:00:00Z',
            nota: 'Tipo de cambio',
          },
        ]),
      ),
      http.post(`${BASE}/r-1/diferencias/d-1/revisada`, async ({ request }) => {
        cuerpo = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ ...cancelada, revisada: true, nota: 'Se canceló en el ERP' });
      }),
    );

    render(<ConciliacionSat />, { wrapper: createQueryWrapper() });

    const fila = await screen.findByTestId('diferencia-d-1');
    expect(within(fila).getByText('Estatus distinto')).toBeInTheDocument();
    expect(within(fila).getByText('FV-000101')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('diferencia-d-2')).getByText('Sin folio en el ERP'),
    ).toBeInTheDocument();
    expect(screen.getByText('Con diferencias por revisar')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('diferencia-d-2')).getByText('Moneda no informada por el SAT'),
    ).toBeInTheDocument();
    const usd = screen.getByTestId('diferencia-d-3');
    expect(within(usd).getByText('USD (moneda del CFDI)')).toBeInTheDocument();
    expect(within(usd).getByText(/Ana Pruebas/)).toBeInTheDocument();

    fireEvent.click(within(fila).getByRole('button', { name: 'Marcar revisada' }));
    fireEvent.change(within(fila).getByLabelText('Nota de la revisión'), {
      target: { value: ' Se canceló en el ERP ' },
    });
    fireEvent.click(within(fila).getByRole('button', { name: 'Marcar revisada' }));

    await waitFor(() => expect(cuerpo).toEqual({ nota: 'Se canceló en el ERP' }));
  });

  it('una corrida sin diferencias lo dice y no muestra tabla', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json([
          {
            ...corrida,
            coincidentes: 12,
            conDiferencias: 0,
            sinDiferencias: true,
            diferenciasPendientes: 0,
            porTipo: [],
          },
        ]),
      ),
    );

    render(<ConciliacionSat />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Sin diferencias')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(
      'cuadran en estatus, total, tipo y receptor',
    );
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('pide archivo y periodo antes de conciliar y manda ambos al servidor', async () => {
    let campos: Record<string, string> | null = null;
    mswServer.use(
      http.get(BASE, () => HttpResponse.json([])),
      http.post(BASE, async ({ request }) => {
        // Se lee el multipart como texto: el parser de undici (request.formData) no acepta el File de jsdom,
        // que además viaja sin nombre ni contenido; se verifica que la parte del archivo vaya en el envío.
        const cuerpo = await request.text();
        const campo = (nombre: string) =>
          new RegExp(`name="${nombre}"\r\n\r\n([^\r]*)`).exec(cuerpo)?.[1] ?? '';
        campos = {
          desde: campo('desde'),
          hasta: campo('hasta'),
          archivo: /name="archivo"; filename="/.test(cuerpo) ? 'adjunto' : 'falta',
        };
        return HttpResponse.json({ ...corrida, id: 'r-2' });
      }),
    );

    render(<ConciliacionSat />, { wrapper: createQueryWrapper() });
    // Esperas con margen explícito por si la CI va cargada (el setup usa 3 s).
    await screen.findByText(/Aún no hay conciliaciones/, undefined, { timeout: ESPERA });

    const conciliar = screen.getByRole('button', { name: 'Conciliar' });
    expect(conciliar).toBeDisabled();
    expect(screen.getByText('Elige el archivo de metadatos del SAT.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Desde *'), { target: { value: '2026-11-02' } });
    fireEvent.change(screen.getByLabelText('Hasta *'), { target: { value: '2026-11-08' } });
    const archivo = new File(['Uuid~RfcEmisor'], 'emitidos.txt', { type: 'text/plain' });
    fireEvent.change(screen.getByLabelText('Archivo del SAT *'), { target: { files: [archivo] } });
    expect(conciliar).toBeEnabled();
    fireEvent.click(conciliar);

    await waitFor(
      () =>
        expect(campos).toEqual({
          desde: '2026-11-02',
          hasta: '2026-11-08',
          archivo: 'adjunto',
        }),
      { timeout: ESPERA },
    );
  });
});
