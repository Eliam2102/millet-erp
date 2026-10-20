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

describe('<PendientesProveedorPanel>', () => {
  it('sin rfc no renderiza nada', () => {
    const { container } = render(
      <PendientesProveedorPanel rfc={null} ordenCompraId={null} onElegir={vi.fn()} />,
      { wrapper: createQueryWrapper() },
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('muestra estado vacío', async () => {
    responder([]);
    render(<PendientesProveedorPanel rfc="XAXX010101000" ordenCompraId={null} onElegir={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByText('Este proveedor no tiene OC pendientes de facturar.')).toBeInTheDocument();
  });

  it('lista OC con recepciones y al clic llama onElegir', async () => {
    responder([oc(1), oc(2)]);
    const onElegir = vi.fn();
    render(<PendientesProveedorPanel rfc="XAXX010101000" ordenCompraId="oc-2" onElegir={onElegir} />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByText('REC-1')).toBeInTheDocument();
    expect(screen.getByText('REC-2')).toBeInTheDocument();
    expect(screen.getByText('sin factura')).toBeInTheDocument();
    const botones = screen.getAllByRole('button');
    expect(botones).toHaveLength(2);
    expect(botones[1]).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(botones[0]);
    expect(onElegir).toHaveBeenCalledWith(oc(1), { id: 'prov', nombre: 'Proveedor Demo' });
  });
});
