import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PendientesProveedorPanel } from './PendientesProveedorPanel';
import type { OcPendienteFacturar } from '@/features/cxp/api/types';

const oc = (n: number): OcPendienteFacturar => ({
  ocId: `oc-${n}`,
  folio: `OC-${n}`,
  sucursalId: `suc-${n}`,
  moneda: 'MXN',
  total: 100 * n,
  cantidadPorFacturar: n,
  recepciones: [
    { recepcionId: `r-${n}`, folio: `REC-${n}`, fecha: '2026-06-01', cantidadRecibida: n, facturaPendiente: n === 1 },
  ],
});

function responder(ocs: OcPendienteFacturar[]) {
  mswServer.use(
    http.get('*/api/v1/cuentas-por-pagar/facturas/pendientes-por-proveedor', () =>
      HttpResponse.json({ proveedorId: 'prov', proveedorNombre: 'Proveedor Demo', ocs }),
    ),
  );
}

const base = { proveedorId: null, recepcionId: null, onElegirRecepcion: vi.fn() };

describe('<PendientesProveedorPanel>', () => {
  it('sin rfc ni proveedor no renderiza ni pide', () => {
    let pidio = false;
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/facturas/pendientes-por-proveedor', () => {
        pidio = true;
        return HttpResponse.json({});
      }),
    );
    const { container } = render(
      <PendientesProveedorPanel {...base} rfc={null} ordenCompraId={null} onElegir={vi.fn()} />,
      { wrapper: createQueryWrapper() },
    );
    expect(container).toBeEmptyDOMElement();
    expect(pidio).toBe(false);
  });

  it('con proveedorId pide ?proveedorId=', async () => {
    let url = '';
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/facturas/pendientes-por-proveedor', ({ request }) => {
        url = request.url;
        return HttpResponse.json({ proveedorId: 'prov', proveedorNombre: 'Proveedor Demo', ocs: [] });
      }),
    );
    render(
      <PendientesProveedorPanel {...base} rfc={null} proveedorId="prov" ordenCompraId={null} onElegir={vi.fn()} />,
      { wrapper: createQueryWrapper() },
    );
    expect(await screen.findByText('Este proveedor no tiene OC pendientes de facturar.')).toBeInTheDocument();
    expect(url).toContain('proveedorId=prov');
    expect(url).not.toContain('rfc=');
  });

  it('muestra estado vacío', async () => {
    responder([]);
    render(
      <PendientesProveedorPanel {...base} rfc="XAXX010101000" ordenCompraId={null} onElegir={vi.fn()} />,
      { wrapper: createQueryWrapper() },
    );
    expect(await screen.findByText('Este proveedor no tiene OC pendientes de facturar.')).toBeInTheDocument();
  });

  it('elegir OC y elegir recepción llaman a su callback; la elegida lleva aria-pressed', async () => {
    responder([oc(1), oc(2)]);
    const onElegir = vi.fn();
    const onElegirRecepcion = vi.fn();
    render(
      <PendientesProveedorPanel
        {...base}
        rfc="XAXX010101000"
        ordenCompraId="oc-2"
        recepcionId="r-2"
        onElegir={onElegir}
        onElegirRecepcion={onElegirRecepcion}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(await screen.findByText('REC-1')).toBeInTheDocument();
    expect(screen.getByText('sin factura')).toBeInTheDocument();
    const rec2 = screen.getByRole('button', { name: /REC-2/ });
    expect(rec2).toHaveAttribute('aria-pressed', 'true');
    // Con recepción elegida, la OC deja de marcarse como elegida.
    expect(screen.getByRole('button', { name: /OC-2/ })).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(screen.getByRole('button', { name: /OC-1/ }));
    expect(onElegir).toHaveBeenCalledWith(oc(1), { id: 'prov', nombre: 'Proveedor Demo' });
    fireEvent.click(screen.getByRole('button', { name: /REC-1/ }));
    expect(onElegirRecepcion).toHaveBeenCalledWith(oc(1), oc(1).recepciones[0], {
      id: 'prov',
      nombre: 'Proveedor Demo',
    });
  });

  it('una OC sin recepciones avisa que no hay qué elegir', async () => {
    responder([{ ...oc(1), recepciones: [] }]);
    render(
      <PendientesProveedorPanel {...base} rfc="XAXX010101000" ordenCompraId={null} onElegir={vi.fn()} />,
      { wrapper: createQueryWrapper() },
    );
    expect(await screen.findByText('Sin recepciones libres para elegir.')).toBeInTheDocument();
  });
});
