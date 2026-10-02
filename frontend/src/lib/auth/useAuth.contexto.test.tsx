import { act, renderHook } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { rutaPermitida } from '@/lib/nav';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { LoginResponse } from '@/lib/auth/types';
import { useAuth } from './useAuth';

const mocks = vi.hoisted(() => ({
  apiFetchJson: vi.fn(),
  clear: vi.fn(),
  navigate: vi.fn(),
}));

vi.mock('@/lib/auth/api-client', () => ({ apiFetchJson: mocks.apiFetchJson }));
vi.mock('@/lib/query-client', () => ({ queryClient: { clear: mocks.clear } }));
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => mocks.navigate }));
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { getActiveAccount: () => null, getAllAccounts: () => [] } }),
}));
vi.mock('@/lib/auth/config', () => ({ authMode: 'FakeForLocalDev', apiScopes: [] }));

const RUTA_A = '/compras/requisiciones'; // solo empresa A
const RUTA_B = '/cxp/facturas'; // solo empresa B

const sesion = (empresaId: string, permisos: string[]): LoginResponse => ({
  accessToken: `tok-${empresaId}`,
  expiresAt: '2030-01-01T00:00:00Z',
  usuario: { id: 'u1', email: 'a@b.c', nombre: 'U' },
  empresas: [
    { id: 'A', rfc: 'AAA', razonSocial: 'A', esLaActual: empresaId === 'A' },
    { id: 'B', rfc: 'BBB', razonSocial: 'B', esLaActual: empresaId === 'B' },
  ],
  permisos,
  comprasSettings: null,
});

beforeEach(() => {
  vi.clearAllMocks();
  useAuthStore.getState().setSession(sesion('A', [PermisosCanonicos.ComprasRequisicionesLeer]));
});

it('D1: changeEmpresa aplica la sesion nueva, limpia cache despues y cambia el acceso a rutas', async () => {
  const orden: string[] = [];
  mocks.apiFetchJson.mockResolvedValue(sesion('B', [PermisosCanonicos.CuentasPorPagarFacturasLeer]));
  mocks.clear.mockImplementation(() => orden.push(`clear:${useAuthStore.getState().currentEmpresaId}`));

  expect(rutaPermitida(RUTA_A, useAuthStore.getState().permisos)).toBe(true);
  expect(rutaPermitida(RUTA_B, useAuthStore.getState().permisos)).toBe(false);

  const { result } = renderHook(() => useAuth());
  await act(() => result.current.changeEmpresa('B'));

  expect(mocks.apiFetchJson).toHaveBeenCalledWith('/api/auth/cambiar-empresa', {
    method: 'POST',
    body: JSON.stringify({ empresaId: 'B' }),
  });
  const { permisos, currentEmpresaId } = useAuthStore.getState();
  expect(currentEmpresaId).toBe('B');
  expect(permisos).toEqual([PermisosCanonicos.CuentasPorPagarFacturasLeer]);
  // clear se invoco una vez, ya con la sesion nueva aplicada
  expect(orden).toEqual(['clear:B']);
  expect(rutaPermitida(RUTA_A, permisos)).toBe(false);
  expect(rutaPermitida(RUTA_B, permisos)).toBe(true);
});

it('D2: logout invoca queryClient.clear()', async () => {
  const { result } = renderHook(() => useAuth());
  await act(() => result.current.logout());

  expect(mocks.clear).toHaveBeenCalledTimes(1);
  expect(useAuthStore.getState().permisos).toEqual([]);
});
