import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PublicClientApplication } from '@azure/msal-browser';

vi.mock('@/lib/auth/config', () => ({
  authMode: 'EntraId',
  apiBaseUrl: 'https://api.test',
  apiScopes: ['api://millet/access_as_user'],
}));

import { apiFetch } from '@/lib/auth/api-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  ESPERA_MINIMA_MS,
  MARGEN_RENOVACION_MS,
  iniciarRenovacionAutomatica,
  msHastaRenovar,
  registrarInstanciaMsal,
  renovarSesion,
} from '@/lib/auth/renovacion-sesion';
import type { LoginResponse } from '@/lib/auth/types';

/**
 * 86e3x819w — el token del ERP dura 15 min y se renueva en silencio con MSAL,
 * sin mover empresa ni sucursal (el formulario abierto no se pierde).
 */

const EMPRESA = 'emp-1';
const fetchMock = vi.fn<typeof fetch>();
const acquireTokenSilent = vi.fn();

function msalFalso(conCuenta = true): PublicClientApplication {
  const cuenta = { homeAccountId: 'h', username: 'u@test' };
  return {
    getActiveAccount: () => (conCuenta ? cuenta : null),
    getAllAccounts: () => (conCuenta ? [cuenta] : []),
    acquireTokenSilent,
  } as unknown as PublicClientApplication;
}

function sesion(token: string, minutos = 15, empresaId = EMPRESA): LoginResponse {
  return {
    accessToken: token,
    expiresAt: new Date(Date.now() + minutos * 60_000).toISOString(),
    usuario: { id: 'u', email: 'u@test', nombre: 'Uziel' },
    empresas: [{ id: empresaId, rfc: 'AAA010101AAA', razonSocial: 'Millet', esLaActual: true }],
    permisos: ['facturacion.caja.operar'],
    comprasSettings: null,
  };
}

const json = (cuerpo: unknown, status = 200) =>
  new Response(JSON.stringify(cuerpo), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  acquireTokenSilent.mockReset();
  acquireTokenSilent.mockResolvedValue({ accessToken: 'entra-nuevo' });
  registrarInstanciaMsal(msalFalso());
  useAuthStore.getState().setSession(sesion('erp-viejo', 1));
  useAuthStore.getState().setCurrentSucursalId('suc-mid');
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  registrarInstanciaMsal(null);
  useAuthStore.getState().clearSession();
});

describe('renovarSesion', () => {
  it('cambia el token por uno nuevo con la misma empresa y conserva la sucursal elegida', async () => {
    fetchMock.mockResolvedValueOnce(json(sesion('erp-nuevo')));

    expect(await renovarSesion()).toBe(true);

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(String(url)).toBe('https://api.test/api/auth/sesion');
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({ entraToken: 'entra-nuevo', empresaId: EMPRESA });
    const estado = useAuthStore.getState();
    expect(estado.accessToken).toBe('erp-nuevo');
    expect(estado.currentEmpresaId).toBe(EMPRESA);
    expect(estado.currentSucursalId).toBe('suc-mid');
    expect(estado.status).toBe('authenticated');
  });

  it('varias peticiones a la vez hacen una sola renovación', async () => {
    fetchMock.mockResolvedValue(json(sesion('erp-nuevo')));

    await Promise.all([renovarSesion(), renovarSesion(), renovarSesion()]);

    expect(acquireTokenSilent).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('si MSAL exige interacción no toca la sesión y devuelve false', async () => {
    acquireTokenSilent.mockRejectedValueOnce(new Error('interaction_required'));

    expect(await renovarSesion()).toBe(false);
    expect(useAuthStore.getState().accessToken).toBe('erp-viejo');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('sin cuenta de MSAL o sin instancia no intenta nada', async () => {
    registrarInstanciaMsal(msalFalso(false));
    expect(await renovarSesion()).toBe(false);
    registrarInstanciaMsal(null);
    expect(await renovarSesion()).toBe(false);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('un intento fallido sin cuenta no bloquea la siguiente renovación', async () => {
    registrarInstanciaMsal(msalFalso(false));
    expect(await renovarSesion()).toBe(false);

    registrarInstanciaMsal(msalFalso());
    fetchMock.mockResolvedValueOnce(json(sesion('erp-nuevo')));
    expect(await renovarSesion()).toBe(true);
    expect(useAuthStore.getState().accessToken).toBe('erp-nuevo');
  });

  it('si el API rechaza la sesión devuelve false', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 403 }));
    expect(await renovarSesion()).toBe(false);
    expect(useAuthStore.getState().accessToken).toBe('erp-viejo');
  });
});

describe('msHastaRenovar', () => {
  it('un token de 15 min se renueva 2 min antes de vencer', () => {
    const ahora = Date.now();
    expect(msHastaRenovar(new Date(ahora + 15 * 60_000), ahora)).toBe(15 * 60_000 - MARGEN_RENOVACION_MS);
  });

  it('un token ya vencido o por vencer espera el mínimo, no renueva en ráfaga', () => {
    const ahora = Date.now();
    expect(msHastaRenovar(new Date(ahora - 60_000), ahora)).toBe(ESPERA_MINIMA_MS);
  });
});

describe('iniciarRenovacionAutomatica', () => {
  it('renueva sola antes de que venza el token', async () => {
    // Sólo el reloj y setTimeout: si se falsea setImmediate, el Response de
    // Node no puede leer su cuerpo y la renovación queda colgada.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    fetchMock.mockResolvedValue(json(sesion('erp-nuevo')));
    useAuthStore.getState().setSession(sesion('erp-15min', 15));
    const detener = iniciarRenovacionAutomatica();
    try {
      await vi.advanceTimersByTimeAsync(15 * 60_000 - MARGEN_RENOVACION_MS - 1_000);
      expect(acquireTokenSilent).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(2_000);
      vi.useRealTimers(); // la renovación ya arrancó; el resto son promesas
      await vi.waitFor(() => expect(useAuthStore.getState().accessToken).toBe('erp-nuevo'));
      expect(acquireTokenSilent).toHaveBeenCalledTimes(1);
      expect(fetchMock).toHaveBeenCalledTimes(1);
    } finally {
      detener();
    }
  });
});

describe('apiFetch con token vencido', () => {
  it('ante un 401 renueva una vez y repite la llamada con el token nuevo', async () => {
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 })) // llamada original
      .mockResolvedValueOnce(json(sesion('erp-nuevo'))) // POST /api/auth/sesion
      .mockResolvedValueOnce(json({ ok: true })); // reintento

    const r = await apiFetch('/api/v1/facturacion/caja');

    expect(r.status).toBe(200);
    const reintento = fetchMock.mock.calls[2]![1] as RequestInit;
    expect((reintento.headers as Headers).get('Authorization')).toBe('Bearer erp-nuevo');
    expect(useAuthStore.getState().status).toBe('authenticated');
  });

  it('si no se puede renovar, limpia la sesión (vuelve al login)', async () => {
    acquireTokenSilent.mockRejectedValueOnce(new Error('interaction_required'));
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }));

    const r = await apiFetch('/api/v1/facturacion/caja');

    expect(r.status).toBe(401);
    expect(useAuthStore.getState().accessToken).toBeNull();
  });

  it('un 401 de un endpoint de auth no intenta renovar', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 }));

    await apiFetch('/api/auth/me');

    expect(acquireTokenSilent).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
