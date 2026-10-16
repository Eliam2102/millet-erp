import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { UserMenu } from './UserMenu';

const { logout } = vi.hoisted(() => ({ logout: vi.fn() }));
vi.mock('@/lib/auth/useAuth', () => ({
  useAuth: () => ({ user: { nombre: 'Cuenta de ensayo', email: 'ensayo@example.invalid' }, logout }),
}));

afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

function abrirMenu() {
  fireEvent.keyDown(screen.getByRole('button', { name: 'Menú de usuario' }), { key: 'Enter' });
}

describe('UserMenu · versión de la API', () => {
  it('muestra commit corto y ambiente en el pie y permite cerrar sesión', async () => {
    const fetchVersion = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      commit: '5ee7270f488b0445', fechaCompilacion: '2026-10-15T12:00:00Z', ambiente: 'Development',
    })));
    vi.stubGlobal('fetch', fetchVersion);
    render(<UserMenu />);
    abrirMenu();
    expect(await screen.findByText('Versión 5ee7270 · Desarrollo')).toHaveClass('text-xs', 'text-ink-muted');
    expect(fetchVersion).toHaveBeenCalledWith(expect.stringContaining('/api/version'), expect.any(Object));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Cerrar sesión' }));
    expect(logout).toHaveBeenCalledOnce();
  });

  it.each(['red', 'http', 'sin commit'] as const)('mantiene el menú utilizable si falta la versión: %s', async (fallo) => {
    const fetchVersion = vi.fn();
    if (fallo === 'red') fetchVersion.mockRejectedValue(new TypeError('API no disponible'));
    else if (fallo === 'http') fetchVersion.mockResolvedValue(new Response(null, { status: 503 }));
    else fetchVersion.mockResolvedValue(new Response(JSON.stringify({ commit: null, ambiente: 'Development' })));
    vi.stubGlobal('fetch', fetchVersion);
    render(<UserMenu />);
    await act(async () => { abrirMenu(); });
    await waitFor(() => expect(fetchVersion).toHaveBeenCalledOnce());
    expect(screen.queryByText(/Versión/)).not.toBeInTheDocument();
    expect(screen.getByText('Cuenta de ensayo')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('menuitem', { name: 'Cerrar sesión' }));
    expect(logout).toHaveBeenCalledOnce();
  });
});
