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
  permisos: [],
  comprasSettings: null,
};

interface AuthActions {
  setStatus: (status: AuthStatus, errorMessage?: string | null) => void;
  setSession: (response: LoginResponse) => void;
  updateEmpresas: (empresas: EmpresaInfo[]) => void;
  updatePermisos: (permisos: string[]) => void;
  updateComprasSettings: (settings: ComprasSettings | null) => void;
  clearSession: () => void;
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
    const empresaActual = response.empresas.find((e) => e.esLaActual);
    set({
      status: 'authenticated',
      errorMessage: null,
      accessToken: response.accessToken,
      expiresAt: new Date(response.expiresAt),
      user: response.usuario,
      empresas: response.empresas,
      currentEmpresaId: empresaActual?.id ?? null,
      permisos: response.permisos,
      comprasSettings: response.comprasSettings,
    });
  },

  updateEmpresas: (empresas) => {
    const empresaActual = empresas.find((e) => e.esLaActual);
    set({
      empresas,
      currentEmpresaId: empresaActual?.id ?? null,
    });
  },

  updatePermisos: (permisos) => set({ permisos }),

  updateComprasSettings: (settings) => set({ comprasSettings: settings }),

  clearSession: () => set(initialState),
}));
