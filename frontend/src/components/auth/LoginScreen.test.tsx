import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { LoginScreen } from './LoginScreen';

const mocks = vi.hoisted(() => ({
  authMode: 'EntraId',
  loginWithEntra: vi.fn(),
  logout: vi.fn(),
  navigate: vi.fn(),
  useAuth: vi.fn(),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mocks.navigate,
}));

vi.mock('@/lib/auth/config', () => ({
  get authMode() {
    return mocks.authMode;
  },
}));

vi.mock('@/lib/auth/useAuth', () => ({
  useAuth: () => mocks.useAuth(),
}));

vi.mock('@/components/auth/DevUserSelector', () => ({
  DevUserSelector: () => <button type="button">Usuario de prueba</button>,
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.authMode = 'EntraId';
  mocks.useAuth.mockReturnValue({
    loginWithEntra: mocks.loginWithEntra,
    logout: mocks.logout,
    isLoading: false,
    errorMessage: null,
    status: 'unauthenticated',
    isAuthenticated: false,
  });
});

describe('LoginScreen', () => {
  it('inicia el flujo corporativo al pulsar el botón Microsoft', () => {
    render(<LoginScreen />);

    fireEvent.click(screen.getByRole('button', { name: 'Continuar con Microsoft' }));

    expect(mocks.loginWithEntra).toHaveBeenCalledOnce();
    expect(screen.queryByRole('button', { name: 'Usuario de prueba' })).not.toBeInTheDocument();
  });

  it('bloquea otro intento mientras conecta y permite cambiar de cuenta tras un error', () => {
    mocks.useAuth.mockReturnValue({
      loginWithEntra: mocks.loginWithEntra,
      logout: mocks.logout,
      isLoading: true,
      errorMessage: 'No se pudo iniciar sesión',
      status: 'error',
      isAuthenticated: false,
    });
    const { rerender } = render(<LoginScreen />);

    expect(screen.getByRole('button', { name: 'Conectando con Microsoft…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cambiar de cuenta Microsoft' })).toBeDisabled();
    expect(screen.getByText('No se pudo iniciar sesión')).toBeInTheDocument();

    mocks.useAuth.mockReturnValue({
      loginWithEntra: mocks.loginWithEntra,
      logout: mocks.logout,
      isLoading: false,
      errorMessage: 'No se pudo iniciar sesión',
      status: 'error',
      isAuthenticated: false,
    });
    rerender(<LoginScreen />);
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar de cuenta Microsoft' }));
    expect(mocks.logout).toHaveBeenCalledOnce();
  });

  it('muestra sólo el selector local en modo de desarrollo', () => {
    mocks.authMode = 'FakeForLocalDev';
    render(<LoginScreen />);

    expect(screen.getByRole('button', { name: 'Usuario de prueba' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Continuar con Microsoft' })).not.toBeInTheDocument();
  });
});
