import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { OrdenCompraSelector } from '@/components/erp/selectors/OrdenCompraSelector';

function oc(id: string, folio: string) {
  return {
    id,
    folio,
    folioAnio: 2026,
    estado: 3,
    subEstadoRecepcion: 0,
    subEstadoFacturacion: 0,
    subEstadoPago: 0,
    proveedorId: 'p-1',
    proveedorNombre: 'Aceros del Norte',
    sucursalId: 's-1',
    compradorTitularId: 'u-1',
    moneda: 'MXN',
    fechaDocumento: '2026-05-09',
    referenciaProveedor: null,
  };
}

describe('<OrdenCompraSelector>', () => {
  it('con debounce envía q y pageSize=20 al servidor', async () => {
    const qs: URLSearchParams[] = [];
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', ({ request }) => {
        qs.push(new URL(request.url).searchParams);
        return HttpResponse.json({ items: [oc('oc-1', 'OC-MID2026-000001')], page: 1, pageSize: 20, totalCount: 1 });
      }),
    );
    render(<OrdenCompraSelector value={null} onChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar orden de compra' }));
    fireEvent.change(await screen.findByPlaceholderText('Folio o referencia…'), { target: { value: '2026-04' } });
    await waitFor(() => expect(qs.some((p) => p.get('q') === '2026-04')).toBe(true));
    expect(qs.every((p) => p.get('pageSize') === '20')).toBe(true);
    expect(qs[0].has('q')).toBe(false);
  });

  it('resuelve la etiqueta de la OC seleccionada fuera de la página', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [oc('oc-otra', 'OC-MID2026-000009')], page: 1, pageSize: 20, totalCount: 1 }),
      ),
      http.get('*/api/v1/compras/ordenes/oc-vieja', () =>
        HttpResponse.json({ id: 'oc-vieja', folio: 'OC-MID2026-000002', proveedorRazonSocial: 'Vidrios del Sur' }),
      ),
    );
    render(<OrdenCompraSelector value="oc-vieja" onChange={() => {}} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('OC-MID2026-000002 · Vidrios del Sur')).toBeInTheDocument();
  });

  it('sin resolver, muestra Sin número y nunca el GUID', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
      ),
      http.get('*/api/v1/compras/ordenes/oc-x', () => new HttpResponse(null, { status: 404 })),
    );
    render(<OrdenCompraSelector value="oc-x" onChange={() => {}} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Sin número')).toBeInTheDocument();
    expect(screen.queryByText(/oc-x/)).toBeNull();
  });

  it('muestra el pie con el total cuando hay más resultados', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [oc('oc-1', 'OC-MID2026-000001')], page: 1, pageSize: 20, totalCount: 57 }),
      ),
    );
    render(<OrdenCompraSelector value={null} onChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar orden de compra' }));
    expect(await screen.findByText(/Mostrando 1 de 57\. Escribe el folio para\s+acotar\./)).toBeInTheDocument();
  });
});
