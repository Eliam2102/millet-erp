import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAlertasCartera, useAtenderAlerta } from '@/features/cxc/api/useAlertas';
import { TipoAlertaCartera } from '@/features/cxc/api/types';

const BASE = '*/api/v1/cuentas-por-cobrar/alertas';

const alerta = {
  id: 'al-1',
  clienteId: 'cli-1',
  tipo: TipoAlertaCartera.Solunion90d,
  moneda: 'USD',
  detalle: 'Factura F-100 vencida 92 días (asegurado SOLUNION).',
  disparadaEn: '2026-07-14T06:00:00Z',
  atendida: false,
  atendidaPor: null,
  atendidaEn: null,
  version: 1,
};

describe('useAlertasCartera', () => {
  it('filtra pendientes por atendida=false y tipo', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(BASE, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json({
          items: [alerta],
          offset: 0,
          limit: 200,
          total: 1,
        });
      }),
    );
    const { result } = renderHook(
      () =>
        useAlertasCartera({
          atendida: false,
          tipo: TipoAlertaCartera.Solunion90d,
        }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('atendida')).toBe('false');
    expect(url!.searchParams.get('tipo')).toBe('1');
  });

  it('enabled=false no dispara (gating del landing)', () => {
    const { result } = renderHook(
      () => useAlertasCartera({}, { enabled: false }),
      { wrapper: createQueryWrapper() },
    );
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useAtenderAlerta', () => {
  it('POST atender con X-Expected-Version', async () => {
    let version: string | null = null;
    mswServer.use(
      http.post(`${BASE}/al-1/atender`, ({ request }) => {
        version = request.headers.get('X-Expected-Version');
        return HttpResponse.json({
          ...alerta,
          atendida: true,
          atendidaPor: 'u-1',
          atendidaEn: '2026-07-14T10:00:00Z',
          version: 2,
        });
      }),
    );
    const { result } = renderHook(() => useAtenderAlerta(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'al-1',
      versionEsperada: 1,
      idempotencyKey: 'idem-a1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(version).toBe('1');
    expect(result.current.data?.atendida).toBe(true);
  });
});
