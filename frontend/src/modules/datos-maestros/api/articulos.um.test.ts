import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useActualizarArticulo,
  useCrearArticulo,
} from '@/modules/datos-maestros/api';
import { Naturaleza } from '@/modules/datos-maestros/api/types';

/**
 * ADR-0046 Etapa 1b: el alta/edición de artículo manda `unidadMedidaId` (FK)
 * en el payload, no el string libre `unidadMedidaDefault`.
 */
describe('artículo ↔ unidad: unidadMedidaId viaja en el payload', () => {
  it('useCrearArticulo manda unidadMedidaId (y no unidadMedidaDefault)', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post('*/api/v1/catalogos/articulos', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          { id: 'art-1', clave: 'A-1' },
          { status: 201 },
        );
      }),
    );

    const { result } = renderHook(() => useCrearArticulo(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      payload: {
        clave: 'A-1',
        nombre: 'Test',
        unidadMedidaId: 'unidad-123',
        naturaleza: Naturaleza.Estandar,
        descripcionLarga: null,
        categoria: null,
        precioReferenciaMonto: null,
        precioReferenciaMoneda: null,
      },
      idempotencyKey: 'k-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(body).not.toBeNull();
    expect(body!.unidadMedidaId).toBe('unidad-123');
    expect(body).not.toHaveProperty('unidadMedidaDefault');
  });

  it('useActualizarArticulo manda unidadMedidaId al reasignar', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/catalogos/articulos/art-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { result } = renderHook(() => useActualizarArticulo(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'art-1',
      payload: {
        nombre: 'Test',
        unidadMedidaId: 'unidad-999',
        naturaleza: Naturaleza.Estandar,
        descripcionLarga: null,
        categoria: null,
        precioReferenciaMonto: null,
        precioReferenciaMoneda: null,
        limpiarDescripcionLarga: false,
        limpiarCategoria: false,
        limpiarPrecioReferencia: false,
      },
      idempotencyKey: 'k-2',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(body).not.toBeNull();
    expect(body!.unidadMedidaId).toBe('unidad-999');
  });
});
