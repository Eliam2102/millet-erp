import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleFacturaAnticipo } from '@/features/facturacion/pages/DetalleFacturaAnticipo';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'fa-1' }),
  useSearch: () => ({}),
}));

const detalle = {
  id: 'fa-1',
  folio: 'FANT-2026-000003',
  estado: 'Timbrado',
  uuid: '11111111-2222-3333-4444-555555555555',
  receptorNombre: 'Cliente Maquila',
  receptorRfc: 'AAA010101AAA',
  tipoAnticipo: 'ClientesMxp',
  anticipoId: 'a-1',
  pedidoFacturableId: null,
  descripcion: 'Anticipo de clientes',
  subtotal: 1000,
  impuestosTrasladados: 160,
  total: 1160,
  moneda: 'MXN',
  fechaTimbrado: '2026-07-12T10:00:00Z',
  version: 3,
  timbradoErrorCodigo: null,
  timbradoErrorMensaje: null,
  relaciones: [],
  anticipo: {
    anticipoId: 'a-1',
    clienteId: 'c-1',
    estado: 'Abierto',
    montoCobrado: 1160,
    montoAmortizado: 500,
    saldo: 660,
    saldoDisponible: 660,
    pedidoOrigenRef: 'AW-7',
    obraId: null,
    obraNombre: null,
    vinculaciones: [
      {
        facturaVentaId: 'f-1',
        facturaFolio: 'VEN-000003',
        facturaUuid: '22222222-2222-3333-4444-555555555555',
        facturaEstado: 'Timbrado',
        importe: 500,
        creadoEn: '2026-07-12T11:00:00Z',
        ncAmortizacionId: 'nc-1',
        ncFolio: 'NC-000001',
        ncUuid: '33333333-2222-3333-4444-555555555555',
        ncEstado: 'Timbrado',
      },
    ],
  },
};

function servidor(anticipo: Record<string, unknown> = {}) {
  mswServer.use(
    http.get('*/api/v1/facturacion/anticipos/facturas/fa-1', () =>
      HttpResponse.json({ ...detalle, anticipo: { ...detalle.anticipo, ...anticipo } }),
    ),
    http.get('*/api/v1/facturacion/comprobantes/fa-1/cancelar', () =>
      HttpResponse.json({ estadoSolicitud: null }),
    ),
  );
}

describe('<DetalleFacturaAnticipo> — F6.3 devolución del anticipo (CA6.17)', () => {
  it('devuelve el anticipo: envía importe y cuenta destino a la NC de devolución', async () => {
    let cuerpo: unknown = null;
    servidor();
    mswServer.use(
      http.post('*/api/v1/facturacion/notas-credito/devolucion-anticipo', async ({ request }) => {
        cuerpo = await request.json();
        return HttpResponse.json(
          { id: 'nc-9', estado: 'Timbrado', uuid: 'U', folio: 'NC-9', total: 600, saldoAnticipo: 60 },
          { status: 201 },
        );
      }),
    );
    render(<DetalleFacturaAnticipo />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FANT-2026-000003')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Devolver anticipo' }));
    fireEvent.change(screen.getByLabelText(/Importe/), { target: { value: '600' } });
    fireEvent.change(screen.getByLabelText(/Banco/), { target: { value: 'BBVA' } });
    fireEvent.change(screen.getByLabelText(/CLABE/), { target: { value: '032180000118359719' } });
    fireEvent.change(screen.getByLabelText(/Titular/), { target: { value: 'Cliente Maquila' } });
    fireEvent.click(screen.getByRole('button', { name: 'Emitir NC y solicitar reembolso' }));

    await waitFor(() => expect(cuerpo).not.toBeNull());
    expect(cuerpo).toMatchObject({
      anticipoId: 'a-1',
      montoTotal: 600,
      reembolso: { banco: 'BBVA', clabe: '032180000118359719', titular: 'Cliente Maquila' },
    });
  });

  it('no deja devolver más que el disponible', async () => {
    let llamadas = 0;
    servidor();
    mswServer.use(
      http.post('*/api/v1/facturacion/notas-credito/devolucion-anticipo', () => {
        llamadas++;
        return HttpResponse.json({}, { status: 201 });
      }),
    );
    render(<DetalleFacturaAnticipo />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('FANT-2026-000003')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Devolver anticipo' }));
    fireEvent.change(screen.getByLabelText(/Importe/), { target: { value: '700' } });
    fireEvent.change(screen.getByLabelText(/Banco/), { target: { value: 'BBVA' } });
    fireEvent.change(screen.getByLabelText(/CLABE/), { target: { value: '032180000118359719' } });
    fireEvent.change(screen.getByLabelText(/Titular/), { target: { value: 'Cliente Maquila' } });
    fireEvent.click(screen.getByRole('button', { name: 'Emitir NC y solicitar reembolso' }));
    await new Promise((r) => setTimeout(r, 50));
    expect(llamadas).toBe(0);
    expect(screen.getByRole('button', { name: 'Emitir NC y solicitar reembolso' })).toBeInTheDocument();
  });

  it('lista las devoluciones con el estado del reembolso y oculta el botón sin saldo', async () => {
    servidor({
      estado: 'Amortizado',
      saldo: 0,
      saldoDisponible: 0,
      devoluciones: [
        {
          notaCreditoId: 'nc-9',
          folio: 'NC-9',
          uuid: 'U',
          estado: 'Timbrado',
          total: 660,
          estadoReembolso: 'Pagado',
          reembolsoPagadoEl: '2026-10-31',
          reembolsoReferencia: 'SPEI 55',
          reembolsoMotivoResolucion: null,
        },
      ],
    });
    render(<DetalleFacturaAnticipo />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('NC-9')).toBeInTheDocument());
    expect(screen.getByText('Devoluciones al cliente (1)')).toBeInTheDocument();
    expect(screen.getByTestId('reembolso-nc-NC-9')).toHaveTextContent('Pagado');
    expect(screen.getByTestId('reembolso-nc-NC-9')).toHaveTextContent('SPEI 55');
    expect(screen.queryByRole('button', { name: 'Devolver anticipo' })).not.toBeInTheDocument();
  });
});
