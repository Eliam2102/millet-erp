import { StrictMode, type ReactNode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { AuthBootstrap } from './AuthBootstrap';

const mocks = vi.hoisted(() => ({
  buildMsalInstance: vi.fn(),
  handlePostLoginRedirect: vi.fn(),
  trySilentLogin: vi.fn(),
}));

vi.mock('@/lib/auth/config', () => ({
  authMode: 'EntraId',
  buildMsalInstance: mocks.buildMsalInstance,
}));

vi.mock('@/lib/auth/useAuth', () => ({
  handlePostLoginRedirect: mocks.handlePostLoginRedirect,
  trySilentLogin: mocks.trySilentLogin,
}));

vi.mock('@azure/msal-react', () => ({
  MsalProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

afterEach(() => vi.restoreAllMocks());

it('inicia MSAL una sola vez en StrictMode y permite reintentar un fallo', async () => {
  const initialize = vi.fn()
    .mockRejectedValueOnce(new Error('fallo temporal'))
    .mockResolvedValueOnce(undefined);
  mocks.buildMsalInstance.mockReturnValue({ initialize });
  mocks.handlePostLoginRedirect.mockResolvedValue(false);
  mocks.trySilentLogin.mockResolvedValue(false);
  vi.spyOn(console, 'error').mockImplementation(() => undefined);

  render(
    <StrictMode>
      <AuthBootstrap><span>ERP disponible</span></AuthBootstrap>
    </StrictMode>,
  );

  expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo iniciar el acceso');
  expect(initialize).toHaveBeenCalledTimes(1);
  expect(screen.queryByText('ERP disponible')).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
  await waitFor(() => expect(screen.getByText('ERP disponible')).toBeInTheDocument());
  expect(initialize).toHaveBeenCalledTimes(2);
  expect(mocks.handlePostLoginRedirect).toHaveBeenCalledTimes(1);
  expect(mocks.trySilentLogin).toHaveBeenCalledTimes(1);
});
