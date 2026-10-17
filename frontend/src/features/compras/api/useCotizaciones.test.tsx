import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { comprasKeys } from '@/features/compras/api/keys';
import {
  useComparativoCotizaciones,
  useRegistrarCotizacion,
} from '@/features/compras/api/useCotizaciones';
import type { RegistrarCotizacionRequest } from '@/features/compras/api/types';

const RQ_ID = '11111111-1111-4111-8111-111111111111';
const KEY = '44444444-4444-4444-8444-444444444444';

function crearWrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
}

describe('useComparativoCotizaciones', () => {
  it('hace GET a la URL de la requisición', async () => {
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/:id/cotizaciones', ({ request }) => {
        urlVista = new URL(request.url).pathname;
        return HttpResponse.json({ requisicionId: RQ_ID, cotizaciones: [] });
      }),
    );
    const { wrapper } = crearWrapper();
    const { result } = renderHook(() => useComparativoCotizaciones(RQ_ID), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVista).toBe(`/api/v1/compras/requisiciones/${RQ_ID}/cotizaciones`);
  });
});

describe('useRegistrarCotizacion', () => {
  it('hace POST con Idempotency-Key e invalida el comparativo', async () => {
    let keyVista: string | null = null;
    mswServer.use(
      http.post('*/api/v1/compras/requisiciones/:id/cotizaciones', ({ request }) => {
        keyVista = request.headers.get('Idempotency-Key');
        return HttpResponse.json({ id: 'c1', version: 1 }, { status: 201 });
      }),
    );
    const { client, wrapper } = crearWrapper();
    client.setQueryData(comprasKeys.cotizaciones(RQ_ID), { requisicionId: RQ_ID });
    const { result } = renderHook(() => useRegistrarCotizacion(RQ_ID), { wrapper });
    const values: RegistrarCotizacionRequest = {
      proveedorId: RQ_ID,
      folioProveedor: null,
      moneda: 'MXN',
      fechaCotizacion: '2026-10-17',
      vigenteHasta: '2026-10-27',
      condicionesCredito: null,
      lineas: [],
    };
    result.current.mutate({ values, idempotencyKey: KEY });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(keyVista).toBe(KEY);
    expect(client.getQueryState(comprasKeys.cotizaciones(RQ_ID))?.isInvalidated).toBe(true);
  });
});
