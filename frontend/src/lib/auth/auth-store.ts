import { create } from 'zustand';
import type {
  AuthState,
  AuthStatus,
  ComprasSettings,
  LoginResponse,
  EmpresaInfo,
} from '@/lib/auth/types';

const initialState: AuthState = {
  status: 'idle',
  errorMessage: null,
  accessToken: null,
  expiresAt: null,
  user: null,
  empresas: [],
  currentEmpresaId: null,
  currentSucursalId: null,
  permisos: [],
  comprasSettings: null,
  isSwitchingEmpresa: false,
};

interface AuthActions {
  setStatus: (status: AuthStatus, errorMessage?: string | null) => void;
  setSession: (response: LoginResponse) => void;
  renovarToken: (response: LoginResponse) => void;
  updateEmpresas: (empresas: EmpresaInfo[]) => void;
  updatePermisos: (permisos: string[]) => void;
  updateComprasSettings: (settings: ComprasSettings | null) => void;
  setIsSwitchingEmpresa: (isSwitching: boolean) => void;
  setCurrentSucursalId: (sucursalId: string | null) => void;
  clearSession: (status?: AuthStatus) => void;
}

/**
 * Store de auth — zustand store sin persistencia (memoria pura, ADR-0007).
 * El refresh del browser implica re-login (silent vía MSAL en EntraId mode,
 * manual con DevUserSelector en FakeForLocalDev). Esto evita exponer el
 * JWT del API en localStorage/cookies y reduce superficie de XSS.
 */
export const useAuthStore = create<AuthState & AuthActions>((set) => ({
  ...initialState,

  setStatus: (status, errorMessage = null) =>
    set({ status, errorMessage }),

  setSession: (response) => {
    // LOCAL-ONLY (manuales): keep the fake session across reloads. Never commit.
    if (import.meta.env.VITE_AUTH_MODE === 'FakeForLocalDev') sessionStorage.setItem('fake-session', JSON.stringify(response));
    const empresaActual = response.empresas.find((e) => e.esLaActual);
    set({
      status: 'authenticated',
      errorMessage: null,
      accessToken: response.accessToken,
      expiresAt: new Date(response.expiresAt),
      user: response.usuario,
      empresas: response.empresas,
      currentEmpresaId: empresaActual?.id ?? null,
      currentSucursalId: null,
      permisos: response.permisos,
      comprasSettings: response.comprasSettings,
      isSwitchingEmpresa: false,
    });
  },

  /**
   * 86e3x819w: renovación silenciosa. Cambia el token y su vencimiento (y
   * los permisos y settings vigentes) sin tocar empresa ni sucursal elegidas,
   * para no mover la pantalla ni el formulario que el usuario está llenando.
   * Si la sesión renovada trae otra empresa actual (no debería: se pide la
   * misma), se aplica completa como en el login.
   */
  renovarToken: (response) =>
    set((state) => {
      const empresaActual = response.empresas.find((e) => e.esLaActual);
      const mismaEmpresa = (empresaActual?.id ?? null) === state.currentEmpresaId;
      return {
        status: 'authenticated',
        errorMessage: null,
        accessToken: response.accessToken,
        expiresAt: new Date(response.expiresAt),
        user: response.usuario,
        empresas: response.empresas,
        permisos: response.permisos,
        comprasSettings: response.comprasSettings,
        ...(mismaEmpresa
          ? {}
          : { currentEmpresaId: empresaActual?.id ?? null, currentSucursalId: null }),
      };
    }),

  updateEmpresas: (empresas) => {
    const empresaActual = empresas.find((e) => e.esLaActual);
    set({
      empresas,
      currentEmpresaId: empresaActual?.id ?? null,
      currentSucursalId: null,
    });
  },

  updatePermisos: (permisos) => set({ permisos }),

  updateComprasSettings: (settings) => set({ comprasSettings: settings }),

  setIsSwitchingEmpresa: (isSwitchingEmpresa) => set({ isSwitchingEmpresa }),

  setCurrentSucursalId: (currentSucursalId) => set({ currentSucursalId }),

  clearSession: (status: AuthStatus = 'idle') => { sessionStorage.removeItem('fake-session'); set({ ...initialState, status }); },
}));

// LOCAL-ONLY (manuales): rehydrate the fake session after a reload. Never commit.
if (import.meta.env.VITE_AUTH_MODE === 'FakeForLocalDev') {
  const saved = sessionStorage.getItem('fake-session');
  if (saved) useAuthStore.getState().setSession(JSON.parse(saved));
}
