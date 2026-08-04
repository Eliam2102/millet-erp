import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useHistoricoRequisicion } from '@/features/compras/api/useHistoricoRequisicion';
import { HistoricoTipo } from '@/features/compras/api/types';

const RQ_ID = 'rq-uuid-1';

describe('useHistoricoRequisicion', () => {
  it('200 OK: devuelve la lista de entradas ordenadas por timestamp', async () => {
    mswServer.use(
      http.get(
        `*/api/v1/compras/requisiciones/${RQ_ID}/historico`,
        () =>
          HttpResponse.json([
            {
              tipo: HistoricoTipo.Creada,
              operacion: 'crear',
              entidad: 'Requisicion',
              entidadId: RQ_ID,
              actorId: 'u-1',
              timestamp: '2026-05-09T10:00:00Z',
              cambios: '{}',
              correlationId: 'corr-1',
            },
            {
              tipo: HistoricoTipo.Transmitida,
              operacion: 'actualizar',
              entidad: 'Requisicion',
              entidadId: RQ_ID,
              actorId: 'u-1',
              timestamp: '2026-05-09T11:00:00Z',
              cambios: '{"estado":["Borrador","EnAutorizacion"]}',
              correlationId: 'corr-2',
            },
          ]),
      ),
    );

    const { result } = renderHook(() => useHistoricoRequisicion(RQ_ID), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.[0].tipo).toBe(HistoricoTipo.Creada);
    expect(result.current.data?.[1].tipo).toBe(HistoricoTipo.Transmitida);
  });

  it('id null: el query no se dispara (enabled=false)', () => {
    let llamadas = 0;
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/*/historico', () => {
        llamadas++;
        return HttpResponse.json([]);
      }),
    );

    const { result } = renderHook(() => useHistoricoRequisicion(null), {
      wrapper: createQueryWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
    expect(llamadas).toBe(0);
  });

  it('403 PERMISO_DENEGADO: bubblea ApiError', async () => {
    mswServer.use(
      http.get(
        `*/api/v1/compras/requisiciones/${RQ_ID}/historico`,
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Sin permiso',
              status: 403,
              code: 'PERMISO_DENEGADO',
            },
            {
              status: 403,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    const { result } = renderHook(() => useHistoricoRequisicion(RQ_ID), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(403);
    expect(err.code).toBe('PERMISO_DENEGADO');
  });
});
