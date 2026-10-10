import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CentroCostoPicker } from './CentroCostoPicker';
import { useAuthStore } from '@/lib/auth/auth-store';
const endpoint = '/api/v1/compras/requisiciones/centros-costo/buscar';
describe('ADM08: selector', () => {
  it('sin alcance pinta un campo de solo lectura y no abre selector', () => {
    const change = vi.fn();
    render(<CentroCostoPicker value="a" onChange={change} endpoint={endpoint} soloLectura initialLabel="A — DEMO área" />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('textbox')).toHaveAttribute('readonly');
    expect(screen.getByRole('textbox')).toHaveValue('A — DEMO área');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(change).not.toHaveBeenCalled();
  });
  it('con alcance permite elegir departamento o máquina', async () => {
    useAuthStore.setState({ status: 'authenticated', accessToken: 'test-token', expiresAt: new Date(Date.now() + 3600_000), currentEmpresaId: 'e' });
    mswServer.use(http.get(`*${endpoint}`, () => HttpResponse.json([
      { id: 'a', clave: 'A', nombre: 'DEMO área', nivel: 2, activo: true },
      { id: 'm', clave: 'M', nombre: 'DEMO máquina', nivel: 3, activo: true },
    ])));
    const change = vi.fn();
    render(<CentroCostoPicker value={null} onChange={change} endpoint={endpoint} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByText('M — DEMO máquina'));
    expect(change).toHaveBeenCalledWith('m');
  });
});
