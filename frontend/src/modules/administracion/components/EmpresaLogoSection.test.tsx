import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresaLogoSection } from './EmpresaLogoSection';

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    currentEmpresaId: 'e-1',
    permisos: [],
  });
  mswServer.use(
    http.get('*/api/v1/admin/empresas/e-1/logo', () => new HttpResponse(null, { status: 404 })),
  );
});

describe('Logo para documentos impresos', () => {
  it('sin permiso muestra la sección sin acciones', async () => {
    render(<EmpresaLogoSection empresaId="e-1" />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Sin logo')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Subir logo' })).not.toBeInTheDocument();
  });
  it('con permiso permite subir y rechaza archivos mayores a 1 MB', async () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.AdminEmpresasEditar] });
    render(<EmpresaLogoSection empresaId="e-1" />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('button', { name: 'Subir logo' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Archivo de logo'), {
      target: { files: [new File([new Uint8Array(1048577)], 'demo.png', { type: 'image/png' })] },
    });
    expect(await screen.findByRole('alert')).toHaveTextContent('excede 1 MB');
  });
  it('muestra la imagen y pide confirmación al quitar', async () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.AdminEmpresasEditar] });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:demo');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(false);
    mswServer.use(
      http.get(
        '*/api/v1/admin/empresas/e-1/logo',
        () => new HttpResponse(new Uint8Array([1]), { headers: { 'Content-Type': 'image/png' } }),
      ),
    );
    render(<EmpresaLogoSection empresaId="e-1" />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByAltText('Logo de la empresa para documentos impresos'),
      ).toBeInTheDocument(),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Quitar' }));
    expect(confirmar).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Cambiar' })).toBeInTheDocument();
  });
});
