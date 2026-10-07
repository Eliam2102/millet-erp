import { beforeEach, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/api/error';
import { queryClient } from '@/lib/query-client';

const toast = vi.hoisted(() => ({ warning: vi.fn(), error: vi.fn() }));
vi.mock('sonner', () => ({ toast }));

const problema = (status: number, detail?: string) =>
  new ApiError({ type: 'x', title: 'Error', status, detail }, status);

beforeEach(() => vi.clearAllMocks());

it('un 403 de query pinta aviso de permiso con id fijo', () => {
  queryClient.getQueryCache().config.onError?.(problema(403, 'Falta compras.ordenes.leer'), {} as never);

  expect(toast.warning).toHaveBeenCalledWith('Ya no tienes permiso para esta acción', {
    id: 'sin-permiso',
    description: 'Falta compras.ordenes.leer',
  });
});

it('un 403 de mutacion pinta aviso, y sin detail sugiere volver a iniciar sesion', () => {
  const mutation = { meta: undefined } as never;
  queryClient.getMutationCache().config.onError?.(problema(403), undefined, undefined, mutation, undefined as never);

  expect(toast.warning).toHaveBeenCalledWith(
    'Ya no tienes permiso para esta acción',
    expect.objectContaining({ description: expect.stringContaining('iniciar sesión') }),
  );
});

it('otros errores no pintan aviso de permiso', () => {
  queryClient.getQueryCache().config.onError?.(problema(500), {} as never);

  expect(toast.warning).not.toHaveBeenCalled();
});

it('con toastOnError la mutacion solo muestra su propio toast', () => {
  const mutation = { meta: { toastOnError: true } } as never;
  queryClient.getMutationCache().config.onError?.(problema(403), undefined, undefined, mutation, undefined as never);

  expect(toast.warning).not.toHaveBeenCalled();
  expect(toast.error).toHaveBeenCalledTimes(1);
});
