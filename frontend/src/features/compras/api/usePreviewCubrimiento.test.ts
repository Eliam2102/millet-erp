import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { usePreviewCubrimiento } from '@/features/compras/api/usePreviewCubrimiento';

describe('usePreviewCubrimiento', () => {
  it('enabled=false: NO consulta el endpoint (queda idle)', () => {
    let llamado = false;
    mswServer.use(
      http.get(
        '*/api/v1/compras/requisiciones/:id/cubrimiento-estimado',
        () => {
          llamado = true;
          return HttpResponse.json({
            requisicionId: 'rq-1',
            aplica: true,
            lineas: [],
          });
        },
      ),
    );

    const { result } = renderHook(
      () => usePreviewCubrimiento('rq-1', false),
      { wrapper: createQueryWrapper() },
    );

    // enabled=false (RQ no EnAutorizacion) ⇒ la query no se ejecuta.
    expect(result.current.fetchStatus).toBe('idle');
    expect(llamado).toBe(false);
  });

  it('enabled=true: consulta el endpoint y devuelve el preview', async () => {
    mswServer.use(
      http.get(
        '*/api/v1/compras/requisiciones/:id/cubrimiento-estimado',
        () =>
          HttpResponse.json({
            requisicionId: 'rq-1',
            aplica: true,
            lineas: [
              {
                lineaId: 'l-1',
                articuloId: 'a-1',
                cantidad: 10,
                estimadoDeAlmacen: 4,
                estimadoDeCompra: 6,
                disponible: 4,
              },
            ],
          }),
      ),
    );

    const { result } = renderHook(
      () => usePreviewCubrimiento('rq-1', true),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.data?.aplica).toBe(true));
    expect(result.current.data?.lineas[0].estimadoDeAlmacen).toBe(4);
  });
});
