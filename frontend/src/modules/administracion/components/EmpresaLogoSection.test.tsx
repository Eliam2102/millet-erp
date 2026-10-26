import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresaLogoSection } from './EmpresaLogoSection';
import * as api from '@/lib/api';

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

afterEach(() => vi.restoreAllMocks());

describe('Logo para documentos impresos', () => {
  it('muestra un mensaje cuando el problema no incluye detalle', async () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.AdminEmpresasEditar] });
    mswServer.use(
      http.put('*/api/v1/admin/empresas/e-1/logo', () =>
        HttpResponse.json(
          { title: 'Error', status: 422, code: 'EMPRESA_LOGO_FORMATO' },
          { status: 422 },
        ),
      ),
    );
    render(<EmpresaLogoSection empresaId="e-1" />, { wrapper: createQueryWrapper() });
    await screen.findByText('Sin logo');
    fireEvent.change(screen.getByLabelText('Archivo de logo'), {
      target: { files: [new File(['x'], 'ficticio.png', { type: 'image/png' })] },
    });
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No se pudo guardar el logo. Intenta de nuevo.',
    );
  });
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
  it('sube el archivo, refresca la vista previa y quita el logo confirmado', async () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.AdminEmpresasEditar] });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:demo');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    let cargado = false;
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1/logo', () =>
        cargado
          ? new HttpResponse(new Uint8Array([1]), { headers: { 'Content-Type': 'image/png' } })
          : new HttpResponse(null, { status: 404 }),
      ),
    );
    // jsdom y fetch de Node usan implementaciones incompatibles de File.
    vi.spyOn(api, 'apiRequest').mockImplementation(async (_path, options) => {
      expect(options?.idempotencyKey).toBeTruthy();
      if (options?.method === 'PUT') {
        const archivo = (options.body as FormData).get('archivo');
        expect(archivo).toMatchObject({ name: 'ficticio.png', size: 1, type: 'image/png' });
        cargado = true;
      } else {
        expect(options?.method).toBe('DELETE');
        cargado = false;
      }
      return { data: undefined as never, status: 200 };
    });
    render(<EmpresaLogoSection empresaId="e-1" />, { wrapper: createQueryWrapper() });
    await screen.findByText('Sin logo');
    fireEvent.change(screen.getByLabelText('Archivo de logo'), {
      target: { files: [new File([new Uint8Array([1])], 'ficticio.png', { type: 'image/png' })] },
    });
    await waitFor(() =>
      expect(screen.getByAltText('Logo de la empresa para documentos impresos')).toHaveAttribute(
        'src',
        'blob:demo',
      ),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Quitar' }));
    expect(await screen.findByRole('alertdialog', { name: 'Quitar logo' })).toBeInTheDocument();
    expect(cargado).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Quitar logo' }));
    expect(await screen.findByText('Sin logo')).toBeInTheDocument();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:demo');
  });
  it('muestra la imagen y pide confirmación al quitar', async () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.AdminEmpresasEditar] });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:demo');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const guardar = vi.spyOn(api, 'apiRequest');
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
    expect(await screen.findByRole('alertdialog', { name: 'Quitar logo' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(guardar).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Cambiar' })).toBeInTheDocument();
  });
});
