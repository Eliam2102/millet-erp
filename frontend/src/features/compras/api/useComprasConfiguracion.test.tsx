import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useActualizarModoNivel2,
  useComprasConfiguracion,
} from '@/features/compras/api/useComprasConfiguracion';

const EMPRESA_ID = '55555555-5555-4555-8555-555555555555';
const KEY = '66666666-6666-4666-8666-666666666666';

describe('useComprasConfiguracion', () => {
  it('lee modoNivel2 de la configuración', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/configuracion', () =>
        HttpResponse.json({
          empresaId: EMPRESA_ID,
          autoGenerarOcAlAutorizar: false,
          apartarExistenciaAlAutorizar: true,
          modoNivel2: 'SoloMonto',
        }),
      ),
    );
    const { result } = renderHook(() => useComprasConfiguracion(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.modoNivel2).toBe('SoloMonto');
  });

  it('PATCH envía solo modoNivel2 con Idempotency-Key', async () => {
    let bodyVisto: unknown = null;
    let keyVista: string | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/configuracion', async ({ request }) => {
        bodyVisto = await request.json();
        keyVista = request.headers.get('Idempotency-Key');
        return HttpResponse.json({
          empresaId: EMPRESA_ID,
          autoGenerarOcAlAutorizar: false,
          apartarExistenciaAlAutorizar: true,
          modoNivel2: 'Ambos',
        });
      }),
    );
    const { result } = renderHook(() => useActualizarModoNivel2(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({ modoNivel2: 'Ambos', idempotencyKey: KEY });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(keyVista).toBe(KEY);
    expect(bodyVisto).toEqual({ modoNivel2: 'Ambos' });
  });
});
