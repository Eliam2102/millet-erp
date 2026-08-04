import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import { useCrearRequisicion } from '@/features/compras/api/useCrearRequisicion';
import {
  Clasificacion,
  EstadoRequisicion,
  Prioridad,
} from '@/features/compras/api/types';
import { comprasKeys } from '@/features/compras/api/keys';
import type { CrearRequisicionCommand } from '@/features/compras/api/useCrearRequisicion';

const commandValido: CrearRequisicionCommand = {
  sucursalId: '11111111-1111-4111-8111-111111111111',
  departamentoId: '22222222-2222-4222-8222-222222222222',
  clasificacion: Clasificacion.Servicio,
  prioridad: Prioridad.Normal,
  fechaSolicitud: '2026-05-09T10:00:00Z',
  sucursalCodigo: 'MID',
  folioAnio: 2026,
};

describe('useCrearRequisicion', () => {
  it('201 OK: devuelve { id, folio, ... } e invalida la lista', async () => {
    let bodyVisto: unknown = null;
    let idempotencyKeyVisto: string | null = null;

    mswServer.use(
      http.post('*/api/v1/compras/requisiciones', async ({ request }) => {
        bodyVisto = await request.json();
        idempotencyKeyVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            id: 'rq-new',
            folio: 'MID2026-000099',
            folioAnio: 2026,
            estado: EstadoRequisicion.Borrador,
            version: 1,
          },
          { status: 201 },
        );
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCrearRequisicion(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      command: commandValido,
      idempotencyKey: 'idem-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.id).toBe('rq-new');
    expect(result.current.data?.folio).toBe('MID2026-000099');
    expect(idempotencyKeyVisto).toBe('idem-1');
    expect(bodyVisto).toMatchObject({
      sucursalId: commandValido.sucursalId,
      clasificacion: Clasificacion.Servicio,
      // Campos derivados que requiere el backend (CrearRequisicionCommand
      // §SucursalCodigo + FolioAnio); validados acá para que el comando
      // llegue completo y no caiga al CrearRequisicionValidator.
      sucursalCodigo: 'MID',
      folioAnio: 2026,
    });

    // Verificamos vía spy que el onSuccess invalida la familia
    // ['compras', 'requisiciones'] — bandeja, detalle, etc.
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: comprasKeys.requisiciones(),
    });
  });

  it('422 con errores[]: el caller puede llamar applyServerErrors', async () => {
    mswServer.use(
      http.post('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Validation failed',
            status: 422,
            code: 'CAMPO_INVALIDO',
            errores: [
              {
                campo: 'descripcion',
                codigo: 'MAX_LENGTH',
                mensaje: 'Máximo 500 caracteres',
              },
            ],
          },
          {
            status: 422,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearRequisicion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      command: commandValido,
      idempotencyKey: 'idem-2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as {
      status: number;
      code?: string;
      problem: { errores?: { campo: string }[] };
    };
    expect(err.status).toBe(422);
    expect(err.problem.errores?.[0].campo).toBe('descripcion');
  });

  it('403 SELECCIONAR_REQUISITANTE_DENEGADO: el caller mapea inline', async () => {
    mswServer.use(
      http.post('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Sin permiso',
            status: 403,
            code: 'SELECCIONAR_REQUISITANTE_DENEGADO',
          },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearRequisicion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      command: commandValido,
      idempotencyKey: 'idem-3',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(403);
    expect(err.code).toBe('SELECCIONAR_REQUISITANTE_DENEGADO');
  });
});
