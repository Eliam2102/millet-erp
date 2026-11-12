import type { PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useCentroAvisos } from './useCentroAvisos';
import type { CentroAvisosRespuesta } from './types';
vi.mock('@/lib/api', () => ({ apiRequest: vi.fn() }));
const sync = vi.hoisted(() => vi.fn(async () => undefined));
vi.mock('../audio/sonidoAvisos', () => ({ crearSonidoAvisos: () => ({ soportado: () => true, abrir: vi.fn(), cerrar: vi.fn(), recuperar: vi.fn(), establecerHabilitado: vi.fn(), probar: vi.fn(), sincronizar: sync }) }));
const request = vi.mocked(apiRequest);
const user = { id: 'usuario' } as NonNullable<ReturnType<typeof useAuthStore.getState>['user']>;
function respuesta(empresa = 'empresa', total = 35): CentroAvisosRespuesta {
  return { contexto: { usuarioId: 'usuario', empresaId: empresa, sucursalId: null }, fuentes: [{ id: 'compras', estado: 'Complete' }], coberturaCompleta: true, totalAccionesConocidas: total, totalSinLeerConocido: total, totalInformativosConocidos: 0, totalFiltradoConocido: total, siguienteContinuacion: 'pagina2', items: [], novedades: { hayNovedades: true, cantidadConocida: total, hastaSecSeguro: '35', porFuente: [{ id: 'compras', cantidad: total }] } };
}
let cliente: QueryClient;
function wrapper({ children }: PropsWithChildren) { return <QueryClientProvider client={cliente}>{children}</QueryClientProvider>; }
beforeEach(() => {
  cliente = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }); request.mockReset(); sync.mockClear();
  useAuthStore.setState({ user, currentEmpresaId: 'empresa', currentSucursalId: null, permisos: ['compras.leer'], isSwitchingEmpresa: false });
  request.mockImplementation(async path => ({ data: String(path).endsWith('preferencia-sonido') ? { habilitado: false } : respuesta() }) as never);
});
describe('Consulta contextual del centro', () => {
  it('pagina sin limitar totales ni novedades y envía cursor anterior', async () => {
    const { result, rerender } = renderHook(({ continuacion }) => useCentroAvisos({ condicion: 'Todas', continuacion }), { wrapper, initialProps: { continuacion: undefined as string | undefined } });
    await waitFor(() => expect(result.current.data?.totalSinLeerConocido).toBe(35)); expect(sync).toHaveBeenCalledWith('35', true, [{ id: 'compras', estado: 'Complete' }]);
    rerender({ continuacion: 'pagina2' }); await waitFor(() => expect(request).toHaveBeenCalledWith('/api/v1/notificaciones/sincronizar', expect.objectContaining({ body: expect.objectContaining({ continuacion: 'pagina2', cursorDeteccion: '35', pageSize: 20 }) })));
  });
  it('oculta contexto viejo y descarta respuesta en vuelo al cambiar empresa/permisos', async () => {
    let resolver: ((r: unknown) => void) | undefined;
    request.mockImplementation(async path => {
      if (String(path).endsWith('preferencia-sonido')) return { data: { habilitado: false } } as never;
      if (useAuthStore.getState().currentEmpresaId === 'empresa') return await new Promise<unknown>(resolve => { resolver = resolve; }) as never;
      return { data: respuesta('otra', 0) } as never;
    });
    const { result } = renderHook(() => useCentroAvisos({ condicion: 'Todas' }), { wrapper }); await waitFor(() => expect(resolver).toBeDefined());
    act(() => useAuthStore.setState({ currentEmpresaId: 'otra', permisos: [] })); expect(result.current.data).toBeUndefined();
    await act(async () => { resolver?.({ data: respuesta() }); }); await waitFor(() => expect(result.current.data?.contexto.empresaId).toBe('otra')); expect(result.current.data?.totalAccionesConocidas).toBe(0);
  });
  it('un error oculta datos anteriores', async () => {
    const { result } = renderHook(() => useCentroAvisos({ condicion: 'Todas' }), { wrapper }); await waitFor(() => expect(result.current.data).toBeDefined());
    request.mockRejectedValue(new Error('403')); await act(async () => { await result.current.refetch(); }); await waitFor(() => expect(result.current.isError).toBe(true)); expect(result.current.data).toBeUndefined();
  });
  it('leer envía sucursal sin ejecutar workflow', async () => {
    const { result } = renderHook(() => useCentroAvisos({ condicion: 'Todas' }), { wrapper }); await waitFor(() => expect(result.current.data).toBeDefined());
    await act(async () => { await result.current.leer.mutateAsync('a'.repeat(64)); }); expect(request).toHaveBeenCalledWith(`/api/v1/notificaciones/${'a'.repeat(64)}/leer`, { method: 'POST', body: { sucursalId: null } });
  });
});
