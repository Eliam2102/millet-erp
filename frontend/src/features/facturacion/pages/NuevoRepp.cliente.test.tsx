import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevoRepp } from './NuevoRepp';

/**
 * Tarea 86e3tfd8k — el complemento de pago se arma a partir del cliente:
 * sin cliente no se consultan facturas; con cliente sólo se piden y muestran
 * sus PPD con saldo; al cambiar de cliente se limpian las facturas elegidas.
 */
vi.mock('@/features/facturacion/api/useFacturas', () => ({
  useEmisorDefaults: () => ({
    data: { rfcEmisor: 'AAA010101AAA', razonSocialEmisor: 'Empresa', regimenFiscalEmisor: '601' },
  }),
}));
vi.mock('@/components/erp', () => ({ SucursalSelector: () => <span>Sucursal</span> }));

const RFC_A = 'AAA010101AAA';
const RFC_B = 'BBB020202BBB';

function cliente(id: string, clave: string, razonSocial: string, rfc: string) {
  return {
    id,
    clave,
    razonSocial,
    rfc,
    regimenFiscal: '601',
    codigoPostalFiscal: '97000',
    usoCfdiDefault: 'G03',
    formaPagoDefault: '99',
    metodoPagoDefault: 'PPD',
    monedaDefault: 'MXN',
    esGenerico: rfc === 'XAXX010101000',
    numRegIdTrib: null,
    paisResidencia: null,
    domicilioExtranjeroCalle: null,
    datosFiscalesCompletos: true,
  };
}

function factura(id: string, folio: string, rfc: string, nombre: string, saldo: number) {
  return {
    facturaVentaId: id,
    folio,
    receptorRfc: rfc,
    receptorNombre: nombre,
    moneda: 'MXN',
    total: saldo,
    acreditadoNc: 0,
    pagadoRepp: 0,
    saldo,
    numParcialidadSiguiente: 1,
    fechaTimbrado: '2026-10-01T12:00:00Z',
  };
}

const FACTURAS: Record<string, ReturnType<typeof factura>[]> = {
  [RFC_A]: [factura('fa-1', 'FA-100', RFC_A, 'Cliente Alfa', 1500)],
  [RFC_B]: [factura('fb-1', 'FB-200', RFC_B, 'Cliente Beta', 900)],
};

function montarMsw() {
  const cobrables: (string | null)[] = [];
  mswServer.use(
    http.get('*/api/v1/facturacion/catalogos/clientes', () =>
      HttpResponse.json([
        cliente('c-a', 'CLI-A', 'Cliente Alfa', RFC_A),
        cliente('c-b', 'CLI-B', 'Cliente Beta', RFC_B),
        cliente('c-g', 'CLI-G', 'Público en general', 'XAXX010101000'),
      ]),
    ),
    http.get('*/api/v1/facturacion/repp/facturas-cobrables', ({ request }) => {
      const rfc = new URL(request.url).searchParams.get('receptorRfc');
      cobrables.push(rfc);
      if (rfc == null) {
        return HttpResponse.json([...FACTURAS[RFC_A], ...FACTURAS[RFC_B]]);
      }
      return HttpResponse.json(FACTURAS[rfc] ?? []);
    }),
  );
  return cobrables;
}

async function elegirCliente(razonSocial: string) {
  fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar cliente' }));
  fireEvent.click(await screen.findByText(razonSocial));
}

describe('NuevoRepp · cliente primero (86e3tfd8k)', () => {
  it('sin cliente el picker de facturas está deshabilitado y no consulta facturas cobrables', async () => {
    const cobrables = montarMsw();
    render(<NuevoRepp onClose={() => {}} />, { wrapper: createQueryWrapper() });

    expect(screen.getByRole('combobox', { name: 'Seleccionar factura PPD a cubrir' })).toBeDisabled();
    expect(
      screen.getByText('Elige primero el cliente para ver sus facturas PPD con saldo.'),
    ).toBeInTheDocument();
    // Da tiempo a que una consulta indebida saliera.
    await new Promise((r) => setTimeout(r, 50));
    expect(cobrables).toEqual([]);
  });

  it('al elegir el cliente A consulta con su RFC y sólo lista sus facturas', async () => {
    const cobrables = montarMsw();
    render(<NuevoRepp onClose={() => {}} />, { wrapper: createQueryWrapper() });

    await elegirCliente('Cliente Alfa');
    await waitFor(() => expect(cobrables).toEqual([RFC_A]));

    const picker = screen.getByRole('combobox', { name: 'Seleccionar factura PPD a cubrir' });
    await waitFor(() => expect(picker).toBeEnabled());
    fireEvent.click(picker);
    expect(await screen.findByText('FA-100')).toBeInTheDocument();
    expect(screen.queryByText('FB-200')).not.toBeInTheDocument();
    expect(screen.queryByText(/RFC genérico/)).not.toBeInTheDocument();
  });

  it('al cambiar de cliente se limpian las facturas elegidas', async () => {
    const cobrables = montarMsw();
    render(<NuevoRepp onClose={() => {}} />, { wrapper: createQueryWrapper() });

    await elegirCliente('Cliente Alfa');
    const picker = screen.getByRole('combobox', { name: 'Seleccionar factura PPD a cubrir' });
    await waitFor(() => expect(picker).toBeEnabled());
    fireEvent.click(picker);
    fireEvent.click(await screen.findByText('FA-100'));
    expect(await screen.findByText(/Saldo por cobrar 1500.00 MXN/)).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Seleccionar factura PPD a cubrir' })).toHaveTextContent(
      'FA-100',
    );

    await elegirCliente('Cliente Beta');
    await waitFor(() => expect(cobrables).toContain(RFC_B));
    await waitFor(() =>
      expect(screen.queryByText(/Saldo por cobrar 1500.00 MXN/)).not.toBeInTheDocument(),
    );
    const pickerB = screen.getByRole('combobox', { name: 'Seleccionar factura PPD a cubrir' });
    expect(pickerB).not.toHaveTextContent('FA-100');
    expect(within(pickerB).getByText('Selecciona una factura PPD con saldo…')).toBeInTheDocument();
  });

  it('con RFC genérico muestra el aviso en línea', async () => {
    montarMsw();
    render(<NuevoRepp onClose={() => {}} />, { wrapper: createQueryWrapper() });

    await elegirCliente('Público en general');
    expect(
      await screen.findByText('RFC genérico: se mostrarán las PPD de todos los receptores con este RFC.'),
    ).toBeInTheDocument();
  });
});
