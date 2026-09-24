import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadoInlineForm } from '@/modules/administracion/components/EmpleadoInlineForm';
import { useAuthStore } from '@/lib/auth/auth-store';

describe('<EmpleadoInlineForm>', () => {
  beforeEach(() => {
    sessionStorage.clear();
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
      empresas: [],
      currentEmpresaId: 'e-1',
      permisos: ['identidad.usuarios.crear', 'identidad.asignaciones.administrar'],
      errorMessage: null,
    });
  });

  afterEach(() => {
    sessionStorage.clear();
    useAuthStore.setState({
      status: 'idle',
      accessToken: null,
      expiresAt: null,
      user: null,
      empresas: [],
      currentEmpresaId: null,
      permisos: [],
      errorMessage: null,
    });
  });

  it('permite ingresar solo el nombre de usuario y selecciona uzieltzaboutlook.com por defecto', () => {
    const onCancel = vi.fn();
    render(<EmpleadoInlineForm onCancel={onCancel} />, { wrapper: createQueryWrapper() });

    const emailInput = screen.getByPlaceholderText('juana.perez');
    expect(emailInput).toBeInTheDocument();

    const domainSelect = screen.getByLabelText(/dominio corporativo/i);
    expect(domainSelect).toHaveValue('uzieltzaboutlook.onmicrosoft.com');

    fireEvent.change(emailInput, { target: { value: 'uziel.tza' } });
    expect(emailInput).toHaveValue('uziel.tza');
  });

  it('guarda y restaura el borrador desde sessionStorage', async () => {
    const onCancel = vi.fn();
    sessionStorage.setItem(
      'millet_empleado_wizard_draft_v1',
      JSON.stringify({
        values: { clave: 'EMP-999', nombre: 'Juana', email: '', puestoId: '', jefeDirectoId: '', sucursalId: '', departamentoId: '', usuarioId: '', codigoNomina: '' },
        paso: 0,
        acceso: 0,
      }),
    );

    render(<EmpleadoInlineForm onCancel={onCancel} />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByText(/Se restauró un borrador previo/i),
    ).toBeInTheDocument();
    expect(screen.getByPlaceholderText('EMP-001')).toHaveValue('EMP-999');
  });

  it('permite limpiar el borrador guardado', () => {
    const onCancel = vi.fn();
    sessionStorage.setItem(
      'millet_empleado_wizard_draft_v1',
      JSON.stringify({
        values: { clave: 'EMP-DRAFT', nombre: 'Draft User', email: 'draft@uzieltzaboutlook.com' },
        paso: 0,
      }),
    );

    render(<EmpleadoInlineForm onCancel={onCancel} />, { wrapper: createQueryWrapper() });

    expect(screen.getByText(/Se restauró un borrador previo/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText('EMP-001')).toHaveValue('EMP-DRAFT');

    const limpiarBtn = screen.getByRole('button', { name: /limpiar borrador/i });
    fireEvent.click(limpiarBtn);

    expect(screen.getByPlaceholderText('EMP-001')).toHaveValue('');
    expect(sessionStorage.getItem('millet_empleado_wizard_draft_v1')).toBeNull();
  });
});
