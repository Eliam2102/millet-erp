import { describe, expect, it, vi } from 'vitest';
import type { QueryClient } from '@tanstack/react-query';
import { ApiError } from '@/lib/api/error';
import type { ConflictDialogApi } from '@/components/erp/collaboration/conflict-dialog-context';
import { handleCeCoMutationError } from './handle-conflict';

/**
 * El handler cubre 409 CONCURRENCY_CONFLICT y — el hallazgo de la
 * verificación en vivo — el 428 SIN code (se matchea por STATUS; el
 * backend no manda code en el 428, un if por código jamás dispararía).
 * El 409 de clave duplicada NO abre el diálogo: es error de campo.
 */

function ctx() {
  const openSimple = vi.fn();
  const invalidateQueries = vi.fn();
  return {
    conflictDialog: { openSimple } as unknown as ConflictDialogApi,
    queryClient: { invalidateQueries } as unknown as QueryClient,
    openSimple,
    invalidateQueries,
  };
}

describe('handleCeCoMutationError — 409/428 → recargar con aviso', () => {
  it('409 CONCURRENCY_CONFLICT abre el diálogo y devuelve true', () => {
    const c = ctx();
    const error = new ApiError(
      { title: 'Conflicto', status: 409, code: 'CONCURRENCY_CONFLICT' },
      409,
    );
    expect(handleCeCoMutationError(error, c)).toBe(true);
    expect(c.openSimple).toHaveBeenCalledOnce();
  });

  it('428 SIN code abre el diálogo (matcheo por status — el backend no manda code)', () => {
    const c = ctx();
    const error = new ApiError(
      { title: 'If-Match requerido', status: 428 },
      428,
    );
    expect(handleCeCoMutationError(error, c)).toBe(true);
    expect(c.openSimple).toHaveBeenCalledOnce();
  });

  it('el onRefrescar del diálogo invalida el namespace del módulo', () => {
    const c = ctx();
    handleCeCoMutationError(
      new ApiError({ title: 'x', status: 428 }, 428),
      c,
    );
    const args = c.openSimple.mock.calls[0][0] as { onRefrescar: () => void };
    args.onRefrescar();
    expect(c.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ['centros-costo'],
    });
  });

  it('409 CECO_CLAVE_DUPLICADA NO abre el diálogo (es error de campo, no de recarga)', () => {
    const c = ctx();
    const error = new ApiError(
      { title: 'Clave duplicada', status: 409, code: 'CECO_CLAVE_DUPLICADA' },
      409,
    );
    expect(handleCeCoMutationError(error, c)).toBe(false);
    expect(c.openSimple).not.toHaveBeenCalled();
  });

  it('errores ajenos devuelven false sin tocar el diálogo', () => {
    const c = ctx();
    expect(handleCeCoMutationError(new Error('boom'), c)).toBe(false);
    expect(
      handleCeCoMutationError(
        new ApiError({ title: 'Validación', status: 422 }, 422),
        c,
      ),
    ).toBe(false);
    expect(c.openSimple).not.toHaveBeenCalled();
  });
});
