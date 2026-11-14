import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { useListarFacturas } from '@/features/facturacion/api/useFacturas';
import { useBandejaFacturasAnticipo } from '@/features/facturacion/api/useAnticipos';
import { useListarRepp } from '@/features/facturacion/api/useRepp';
import { useReppPendientes } from '@/features/facturacion/api/useReppPendientes';
import { useListarCartaPorte } from '@/features/facturacion/api/useCartaPorte';
import { useExcepciones } from '@/features/facturacion/api/usePedidos';
import { useSolicitudesCatalogo } from '@/features/contabilidad/api/hooks';
import { useNotasCreditoPorAutorizar } from '@/features/facturacion/api/useNotasCreditoAw';

/** 86e401dz2 — cada bandeja de Facturación manda q, offset y limit y expone el total real del servidor. */
async function capturaUrl<T>(patron: string, respuesta: unknown, usar: () => { isSuccess: boolean; data?: T }) {
  let url: URL | undefined;
  mswServer.use(
    http.get(patron, ({ request }) => {
      url = new URL(request.url);
      return HttpResponse.json(respuesta as Record<string, unknown>);
    }),
  );
  const { result } = renderHook(usar, { wrapper: createQueryWrapper(createTestQueryClient()) });
  await waitFor(() => expect(result.current.isSuccess).toBe(true));
  return { url: url!, data: result.current.data };
}

const envelope = { items: [], sinAsignarCount: null, total: 450 };

describe('bandejas de Facturación paginadas en el servidor', () => {
  it('facturas', async () => {
    const { url, data } = await capturaUrl('*/api/v1/facturacion/facturas', envelope, () =>
      useListarFacturas({ q: 'FCNK-1', offset: 50, limit: 50 }),
    );
    expect(url.searchParams.get('q')).toBe('FCNK-1');
    expect(url.searchParams.get('offset')).toBe('50');
    expect(url.searchParams.get('limit')).toBe('50');
    expect(data?.total).toBe(450);
  });

  it('facturas de anticipo', async () => {
    const { url, data } = await capturaUrl('*/api/v1/facturacion/anticipos/facturas', envelope, () =>
      useBandejaFacturasAnticipo({ q: 'FANT', offset: 50, limit: 50 }),
    );
    expect(url.searchParams.get('q')).toBe('FANT');
    expect(url.searchParams.get('offset')).toBe('50');
    expect(url.searchParams.get('limit')).toBe('50');
    expect(data?.total).toBe(450);
  });

  it('complementos de pago', async () => {
    const { url, data } = await capturaUrl('*/api/v1/facturacion/repp', envelope, () =>
      useListarRepp(undefined, undefined, { q: 'P-1', offset: 50, limit: 50 }),
    );
    expect(url.searchParams.get('q')).toBe('P-1');
    expect(url.searchParams.get('offset')).toBe('50');
    expect(url.searchParams.get('limit')).toBe('50');
    expect(data?.total).toBe(450);
  });

  it('pendientes de REP', async () => {
    const { url, data } = await capturaUrl(
      '*/api/v1/facturacion/repp/pendientes',
      { items: [], total: 450, kpis: { pendientes: 0, cercaDelPlazo: 0, vencidos: 0, conError: 0 } },
      () => useReppPendientes('Pendiente', '', 10, 'cliente'),
    );
    expect(url.searchParams.get('q')).toBe('cliente');
    expect(url.searchParams.get('offset')).toBe('10');
    expect(data?.total).toBe(450);
  });

  it('carta porte', async () => {
    const { url, data } = await capturaUrl('*/api/v1/facturacion/carta-porte', { items: [], total: 450 }, () =>
      useListarCartaPorte(undefined, { q: 'CP-1', offset: 50, limit: 50 }),
    );
    expect(url.searchParams.get('q')).toBe('CP-1');
    expect(url.searchParams.get('offset')).toBe('50');
    expect(url.searchParams.get('limit')).toBe('50');
    expect(data?.total).toBe(450);
  });

  it('excepciones de importación', async () => {
    const { url, data } = await capturaUrl(
      '*/api/v1/facturacion/pedidos-facturables/excepciones',
      { items: [], total: 450 },
      () => useExcepciones(false, { q: 'FPIN', offset: 50, limit: 50 }),
    );
    expect(url.searchParams.get('q')).toBe('FPIN');
    expect(url.searchParams.get('offset')).toBe('50');
    expect(url.searchParams.get('limit')).toBe('50');
    expect(url.searchParams.get('soloResueltas')).toBe('true');
    expect(data?.total).toBe(450);
  });

  it('notas de crédito por autorizar', async () => {
    const { url, data } = await capturaUrl('*/api/v1/facturacion/notas-credito', { items: [], total: 450 }, () =>
      useNotasCreditoPorAutorizar(20),
    );
    expect(url.searchParams.get('offset')).toBe('20');
    expect(url.searchParams.get('limit')).toBe('10');
    expect(data?.total).toBe(450);
  });
});


describe('cantidad elegida enviada al servidor', () => {
  it.each([20, 50, 100])('notas de crédito: %i filas', async (limit) => {
    const { url } = await capturaUrl('*/api/v1/facturacion/notas-credito', envelope,
      () => useNotasCreditoPorAutorizar(0, limit));
    expect(url.searchParams.get('limit')).toBe(String(limit));
  });
  it('REP pendientes conserva filtros con 20 filas', async () => {
    const { url } = await capturaUrl('*/api/v1/facturacion/repp/pendientes', envelope,
      () => useReppPendientes('Pendiente', '', 20, 'cliente', 20));
    expect(url.searchParams.get('limit')).toBe('20');
    expect(url.searchParams.get('offset')).toBe('20');
    expect(url.searchParams.get('q')).toBe('cliente');
  });
  it('solicitudes de catálogo: 50 filas', async () => {
    const { url } = await capturaUrl('*/api/v1/contabilidad/solicitudes', envelope,
      () => useSolicitudesCatalogo('Pendiente', 50, 50));
    expect(url.searchParams.get('limit')).toBe('50');
    expect(url.searchParams.get('offset')).toBe('50');
    expect(url.searchParams.get('estado')).toBe('Pendiente');
  });
});
