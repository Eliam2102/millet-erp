import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadoAccesoPanel } from '@/modules/administracion/components/EmpleadoAccesoPanel';
import { useAuthStore } from '@/lib/auth/auth-store';

describe('<EmpleadoAccesoPanel>', () => {
  beforeEach(() => {
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-1', email: 'admin@millet.mx', nombre: 'Admin' },
      empresas: [],
      currentEmpresaId: 'e-1',
      permisos: [
        'identidad.usuarios.crear',
        'identidad.asignaciones.administrar',
        'identidad.usuarios.editar',
        'compartido.catalogos.leer',
      ],
      errorMessage: null,
    });

    mswServer.use(
      http.get('*/api/v1/identidad/roles*', () =>
        HttpResponse.json({
          items: [
            { id: 'rol-1', nombre: 'Vendedor', activo: true },
            { id: 'rol-2', nombre: 'Administrador', activo: true },
          ],
        }),
      ),
      http.get('*/api/v1/catalogos/departamentos*', () =>
        HttpResponse.json({
          items: [
            { id: 'd-1', nombre: 'Ventas', activo: true },
            { id: 'd-2', nombre: 'Almacén', activo: true },
          ],
        }),
      ),
      http.get('*/api/v1/admin/empresas/sucursales/*/departamentos*', () =>
        HttpResponse.json({
          items: [
            { id: 'd-1', departamentoId: 'd-1', departamentoNombre: 'Ventas', activo: true },
          ],
        }),
      ),
      http.get('*/api/v1/catalogos/puestos*', () =>
        HttpResponse.json({
          items: [
            { id: 'p-1', nombre: 'Ejecutivo de Ventas', activo: true },
          ],
        }),
      ),
      http.get('*/api/v1/admin/empresas/sucursales/*/puestos*', () =>
        HttpResponse.json({
          items: [
            { puestoId: 'p-1', puestoNombre: 'Ejecutivo de Ventas', rolSugeridoEfectivoId: 'rol-1' },
          ],
        }),
      ),
    );
  });

  afterEach(() => {
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

  it('muestra el selector de dominio corporativo y parsea el correo inicial', () => {
    render(
      <EmpleadoAccesoPanel
        empleadoId="emp-1"
        usuarioId={null}
        email="juana.perez@millet.mx"
        emailContacto="juana.personal@gmail.com"
        empleadoActivo={true}
        sucursalId="s-1"
        departamentoId="d-1"
        puestoId="p-1"
      />,
      { wrapper: createQueryWrapper() },
    );

    const emailInput = screen.getByPlaceholderText('juana.perez');
    expect(emailInput).toHaveValue('juana.perez');

    const domainSelect = screen.getByLabelText(/dominio corporativo/i);
    expect(domainSelect).toHaveValue('millet.mx');

    expect(screen.getAllByText('juana.perez@millet.mx').length).toBeGreaterThan(0);
  });

  it('permite cambiar el prefijo y el dominio corporativo bloqueado', () => {
    render(
      <EmpleadoAccesoPanel
        empleadoId="emp-1"
        usuarioId={null}
        email={null}
        empleadoActivo={true}
        sucursalId="s-1"
        departamentoId="d-1"
        puestoId="p-1"
      />,
      { wrapper: createQueryWrapper() },
    );

    const emailInput = screen.getByPlaceholderText('juana.perez');
    expect(emailInput).toHaveValue('');

    const domainSelect = screen.getByLabelText(/dominio corporativo/i);
    expect(domainSelect).toHaveValue('uzieltzaboutlook.onmicrosoft.com');

    fireEvent.change(emailInput, { target: { value: 'carlos.m' } });
    fireEvent.change(domainSelect, { target: { value: 'millet.com.mx' } });

    expect(screen.getByText('carlos.m@millet.com.mx')).toBeInTheDocument();
  });

  it('valida contra Entra ID y muestra estado de cuenta existente vinculable', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/directorio-entra/validar-correo*', () =>
        HttpResponse.json({
          correo: 'roberto@millet.mx',
          dominioPermitido: true,
          cuentaEntra: { objectId: 'entra-123', nombreMostrado: 'Roberto Ruiz', habilitada: true },
          usuarioErp: null,
          empleadoVinculado: null,
          puedeVincularCuentaExistente: true,
          puedeCrearCuentaNueva: false,
          motivoBloqueo: null,
        }),
      ),
    );

    render(
      <EmpleadoAccesoPanel
        empleadoId="emp-1"
        usuarioId={null}
        email="roberto@millet.mx"
        empleadoActivo={true}
        sucursalId="s-1"
        departamentoId="d-1"
        puestoId="p-1"
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      await screen.findByText(/cuenta microsoft encontrada en entra id/i),
    ).toBeInTheDocument();
    expect(screen.getByText('Roberto Ruiz')).toBeInTheDocument();
  });

  it('alerta cuando la cuenta Microsoft ya pertenece a otro colaborador', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/directorio-entra/validar-correo*', () =>
        HttpResponse.json({
          correo: 'roberto@millet.mx',
          dominioPermitido: true,
          cuentaEntra: { objectId: 'entra-123', nombreMostrado: 'Roberto Ruiz', habilitada: true },
          usuarioErp: null,
          empleadoVinculado: { id: 'emp-99', clave: 'EMP-099', nombre: 'Roberto Antiguo' },
          puedeVincularCuentaExistente: false,
          puedeCrearCuentaNueva: false,
          motivoBloqueo: null,
        }),
      ),
    );

    render(
      <EmpleadoAccesoPanel
        empleadoId="emp-1"
        usuarioId={null}
        email="roberto@millet.mx"
        empleadoActivo={true}
        sucursalId="s-1"
        departamentoId="d-1"
        puestoId="p-1"
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      await screen.findByText(/esta cuenta ya está vinculada al colaborador roberto antiguo/i),
    ).toBeInTheDocument();
  });

  it('en modo cuenta nueva muestra disponible para provisión cuando Entra ID no tiene cuenta', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/directorio-entra/validar-correo*', () =>
        HttpResponse.json({
          correo: 'nuevo.usuario@millet.mx',
          dominioPermitido: true,
          cuentaEntra: null,
          usuarioErp: null,
          empleadoVinculado: null,
          puedeVincularCuentaExistente: false,
          puedeCrearCuentaNueva: true,
          motivoBloqueo: null,
        }),
      ),
    );

    render(
      <EmpleadoAccesoPanel
        empleadoId="emp-1"
        usuarioId={null}
        email="nuevo.usuario@millet.mx"
        empleadoActivo={true}
        sucursalId="s-1"
        departamentoId="d-1"
        puestoId="p-1"
      />,
      { wrapper: createQueryWrapper() },
    );

    const modoSelect = screen.getByLabelText(/modo de acceso/i);
    fireEvent.change(modoSelect, { target: { value: '2' } });

    expect(
      await screen.findByText(/correo disponible para provisión de nueva cuenta/i),
    ).toBeInTheDocument();
  });

  it('precarga departamento y puesto iniciales y sugiere rol efectivo del puesto', async () => {
    render(
      <EmpleadoAccesoPanel
        empleadoId="emp-1"
        usuarioId={null}
        email="juana.perez@millet.mx"
        empleadoActivo={true}
        sucursalId="s-1"
        departamentoId="d-1"
        puestoId="p-1"
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(await screen.findByText(/vendedor \(recomendado\)/i)).toBeInTheDocument();
  });
});
