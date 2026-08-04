import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useCollaboration } from '@/components/erp/collaboration/useCollaboration';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * Tests del hook real (UF8-PR1) con un mock del módulo
 * <c>@/lib/signalr</c>. Cubrir: id null no conecta, GetPresence
 * snapshot inicial, mapear modo Editing/Viewing, filtrar al usuario
 * actual, cleanup en unmount, payload de otra entidad ignorado.
 */

// Mock manual del HubConnection con event handlers in-memory.
type Handler = (...args: unknown[]) => void;

const mockConn = {
  state: 'Connected' as const,
  handlers: new Map<string, Handler>(),
  invokeCalls: [] as Array<{ method: string; args: unknown[] }>,
  invokeReturn: {} as Record<string, unknown>,
  on(event: string, h: Handler) {
    this.handlers.set(event, h);
  },
  off(event: string) {
    this.handlers.delete(event);
  },
  async invoke(method: string, ...args: unknown[]) {
    this.invokeCalls.push({ method, args });
    if (method in this.invokeReturn) return this.invokeReturn[method];
    return undefined;
  },
  /** Helper para los tests: simular un evento del hub. */
  emit(event: string, payload: unknown) {
    const h = this.handlers.get(event);
    if (h) h(payload);
  },
  reset() {
    this.handlers.clear();
    this.invokeCalls = [];
    this.invokeReturn = {};
  },
};

vi.mock('@/lib/signalr', () => ({
  getHubConnection: () => Promise.resolve(mockConn),
}));

const ME = 'user-self';
const OTRO = 'user-otro';
const OTRO_2 = 'user-otro-2';

beforeEach(() => {
  mockConn.reset();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: ME, email: 'me@m.com', nombre: 'Yo' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('useCollaboration', () => {
  it('id null: no conecta al hub, devuelve presencia vacía', () => {
    const { result } = renderHook(() => useCollaboration('requisicion', null));
    expect(result.current.viendo).toEqual([]);
    expect(result.current.editando).toEqual([]);
    expect(mockConn.invokeCalls).toHaveLength(0);
  });

  it('id válido: invoca GetPresence + ViewingResource al montar', async () => {
    mockConn.invokeReturn['GetPresence'] = [];
    renderHook(() => useCollaboration('requisicion', 'rq-1'));
    await waitFor(() =>
      expect(
        mockConn.invokeCalls.some((c) => c.method === 'GetPresence'),
      ).toBe(true),
    );
    expect(
      mockConn.invokeCalls.some((c) => c.method === 'ViewingResource'),
    ).toBe(true);
  });

  it('modo editing: invoca EditingResource (no Viewing)', async () => {
    mockConn.invokeReturn['GetPresence'] = [];
    renderHook(() =>
      useCollaboration('requisicion', 'rq-1', { modo: 'editing' }),
    );
    await waitFor(() =>
      expect(
        mockConn.invokeCalls.some((c) => c.method === 'EditingResource'),
      ).toBe(true),
    );
    expect(
      mockConn.invokeCalls.some((c) => c.method === 'ViewingResource'),
    ).toBe(false);
  });

  it('snapshot inicial: mapea Editing/Viewing y filtra al usuario actual', async () => {
    mockConn.invokeReturn['GetPresence'] = [
      {
        userId: OTRO,
        userNombre: 'Pedro García',
        modo: 'Editing',
        sinceUtc: '2026-05-09T10:00:00Z',
        lastSeenUtc: '2026-05-09T10:00:00Z',
      },
      {
        userId: OTRO_2,
        userNombre: 'Ana Pérez',
        modo: 'Viewing',
        sinceUtc: '2026-05-09T10:01:00Z',
        lastSeenUtc: '2026-05-09T10:01:00Z',
      },
      {
        // Self — debe filtrarse.
        userId: ME,
        userNombre: 'Yo',
        modo: 'Viewing',
        sinceUtc: '2026-05-09T10:02:00Z',
        lastSeenUtc: '2026-05-09T10:02:00Z',
      },
    ];

    const { result } = renderHook(() =>
      useCollaboration('requisicion', 'rq-1'),
    );
    await waitFor(() => expect(result.current.editando.length).toBe(1));
    expect(result.current.editando[0].nombre).toBe('Pedro García');
    expect(result.current.viendo).toHaveLength(1);
    expect(result.current.viendo[0].nombre).toBe('Ana Pérez');
    // El usuario actual NO aparece.
    expect(
      [...result.current.viendo, ...result.current.editando].some(
        (u) => u.userId === ME,
      ),
    ).toBe(false);
  });

  it('evento userPresence broadcast actualiza la presencia', async () => {
    mockConn.invokeReturn['GetPresence'] = [];
    const { result } = renderHook(() =>
      useCollaboration('requisicion', 'rq-1'),
    );

    await waitFor(() =>
      expect(mockConn.handlers.has('userPresence')).toBe(true),
    );

    act(() => {
      mockConn.emit('userPresence', {
        entidad: 'requisicion',
        entidadId: 'rq-1',
        users: [
          {
            userId: OTRO,
            userNombre: 'Pedro',
            modo: 'Editing',
            sinceUtc: '2026-05-09T11:00:00Z',
            lastSeenUtc: '2026-05-09T11:00:00Z',
          },
        ],
      });
    });

    expect(result.current.editando).toHaveLength(1);
    expect(result.current.editando[0].nombre).toBe('Pedro');
  });

  it('evento de OTRA entidad/id se ignora', async () => {
    mockConn.invokeReturn['GetPresence'] = [];
    const { result } = renderHook(() =>
      useCollaboration('requisicion', 'rq-1'),
    );

    await waitFor(() =>
      expect(mockConn.handlers.has('userPresence')).toBe(true),
    );

    act(() => {
      mockConn.emit('userPresence', {
        entidad: 'requisicion',
        entidadId: 'rq-OTRA', // distinto id
        users: [
          {
            userId: OTRO,
            userNombre: 'Pedro',
            modo: 'Editing',
            sinceUtc: '2026-05-09T11:00:00Z',
            lastSeenUtc: '2026-05-09T11:00:00Z',
          },
        ],
      });
    });

    expect(result.current.editando).toHaveLength(0);
  });

  it('unmount: invoca LeaveResource y desuscribe el handler', async () => {
    mockConn.invokeReturn['GetPresence'] = [];
    const { unmount } = renderHook(() =>
      useCollaboration('requisicion', 'rq-1'),
    );
    await waitFor(() =>
      expect(mockConn.handlers.has('userPresence')).toBe(true),
    );

    unmount();

    await waitFor(() =>
      expect(
        mockConn.invokeCalls.some((c) => c.method === 'LeaveResource'),
      ).toBe(true),
    );
    expect(mockConn.handlers.has('userPresence')).toBe(false);
  });
});
