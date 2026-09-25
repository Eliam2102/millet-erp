import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
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

describe('<EmpleadoInlineForm> — rol sugerido por el puesto (B4)', () => {
  const SUC_ID = '00000000-0000-0000-0000-0000000000a1';
  const DEPTO_ID = '00000000-0000-0000-0000-0000000000a2';
  const PUESTO_ID = '00000000-0000-0000-0000-0000000000a3';
  const ROL_SUGERIDO_ID = '00000000-0000-0000-0000-0000000000a4';
  const ROL_OTRO_ID = '00000000-0000-0000-0000-0000000000a5';

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

    mswServer.use(
      http.get('*/api/v1/catalogos/sucursales', () =>
        HttpResponse.json({
          items: [{ id: SUC_ID, clave: 'MTY', nombre: 'Monterrey', estatus: 0 }],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
      http.get('*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos', () =>
        HttpResponse.json({
          items: [
            {
              sucursalId: SUC_ID,
              departamentoId: DEPTO_ID,
              departamentoClave: 'VEN',
              departamentoNombre: 'Ventas',
              estatus: 0,
              version: 1,
            },
          ],
          total: 1,
        }),
      ),
      http.get('*/api/v1/admin/empresas/sucursales/:sucursalId/puestos', () =>
        HttpResponse.json({
          items: [
            {
              sucursalId: SUC_ID,
              puestoId: PUESTO_ID,
              puestoClave: 'VEND',
              puestoNombre: 'Vendedor de mostrador',
              departamentoId: DEPTO_ID,
              departamentoNombre: 'Ventas',
              // Sin excepción propia — hereda el rol sugerido del puesto
              // (Parte E: rolSugeridoEfectivoId = asignación ?? puesto).
              rolSugeridoId: null,
              rolSugeridoEfectivoId: ROL_SUGERIDO_ID,
              estatus: 0,
              version: 1,
            },
          ],
          total: 1,
        }),
      ),
      http.get('*/api/v1/catalogos/puestos', () =>
        HttpResponse.json({
          items: [
            {
              id: PUESTO_ID,
              clave: 'VEND',
              nombre: 'Vendedor de mostrador',
              estatus: 0,
              rolSugeridoId: ROL_SUGERIDO_ID,
              rolSugeridoNombre: 'Vendedor',
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
      http.get('*/api/v1/catalogos/departamentos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
      http.get('*/api/v1/catalogos/empleados', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
      http.get('*/api/v1/identidad/roles', () =>
        HttpResponse.json({
          items: [
            { id: ROL_SUGERIDO_ID, codigo: 'VEN', nombre: 'Vendedor', descripcion: null, esDelSistema: false, activo: true, version: 1 },
            { id: ROL_OTRO_ID, codigo: 'SUP', nombre: 'Supervisor', descripcion: null, esDelSistema: false, activo: true, version: 1 },
          ],
          offset: 0,
          limit: 200,
          total: 2,
        }),
      ),
      http.get('*/api/v1/identidad/directorio-entra/validar-correo', () =>
        HttpResponse.json({
          correo: 'vendedor.mty@uzieltzaboutlook.onmicrosoft.com',
          dominioPermitido: true,
          cuentaEntra: { objectId: 'oid-1', nombreMostrado: 'Vendedor Mty', habilitada: true },
          usuarioErp: null,
          puedeVincularCuentaExistente: true,
          puedeCrearCuentaNueva: false,
        }),
      ),
    );
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

  // Sincroniza con las queries en vuelo (sucursales/departamentos/
  // puestos/roles) antes de abrir un combobox — sin esto, un click
  // disparado en el mismo tick que una query aún en curso deja el
  // Popover de Radix abierto-y-cerrado por la re-renderización que
  // llega justo después (falso negativo de "no encontrado").
  async function sincronizar() {
    await waitFor(() => expect(screen.queryByText('Cargando…')).not.toBeInTheDocument());
  }

  it('precarga el rol sugerido con etiqueta, sigue editable y el resumen muestra el rol elegido', async () => {
    render(<EmpleadoInlineForm onCancel={vi.fn()} />, { wrapper: createQueryWrapper() });
    await sincronizar();

    // Paso 0: clave, nombre y sucursal.
    fireEvent.change(screen.getByPlaceholderText('EMP-001'), { target: { value: 'EMP-VEN' } });
    fireEvent.change(screen.getByPlaceholderText('Juana Pérez'), { target: { value: 'Vendedor Mty' } });
    fireEvent.click(screen.getByRole('combobox', { name: /seleccionar sucursal/i }));
    fireEvent.click(await screen.findByText('Monterrey'));
    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    await sincronizar();

    // Paso 1: departamento.
    fireEvent.click(screen.getByRole('combobox', { name: /seleccionar departamento/i }));
    fireEvent.click(await screen.findByText('Ventas'));
    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    await sincronizar();

    // Paso 2: puesto (trae rolSugeridoId).
    fireEvent.click(screen.getByRole('combobox', { name: /seleccionar puesto/i }));
    fireEvent.click(await screen.findByText('Vendedor de mostrador'));
    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    await sincronizar();

    // Paso 3: acceso — el rol se precarga con el sugerido y muestra el hint.
    fireEvent.change(screen.getByLabelText(/acceso al erp/i), { target: { value: '1' } });
    const rolSelect = await screen.findByLabelText(/rol en la empresa/i);
    expect(rolSelect).toHaveValue(ROL_SUGERIDO_ID);
    expect(screen.getByText(/sugerido por el puesto/i)).toBeInTheDocument();

    // El rol sigue siendo editable: cambiarlo quita el hint de sugerencia.
    fireEvent.change(rolSelect, { target: { value: ROL_OTRO_ID } });
    expect(rolSelect).toHaveValue(ROL_OTRO_ID);
    expect(screen.queryByText(/sugerido por el puesto/i)).not.toBeInTheDocument();

    // Vuelve a elegir el sugerido para completar el flujo hasta el resumen.
    fireEvent.change(rolSelect, { target: { value: ROL_SUGERIDO_ID } });

    // Correo corporativo para pasar la validación de dominio del paso 3.
    fireEvent.change(screen.getByPlaceholderText('juana.perez'), {
      target: { value: 'vendedor.mty' },
    });

    await waitFor(
      () => expect(screen.getByText(/cuenta microsoft encontrada/i)).toBeInTheDocument(),
      { timeout: 2000 },
    );

    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));

    // Paso 4: resumen muestra el rol que se va a asignar.
    expect(await screen.findByText(/revisa antes de crear/i)).toBeInTheDocument();
    expect(screen.getByText(/rol a asignar:/i)).toBeInTheDocument();
    expect(screen.getByText('Vendedor')).toBeInTheDocument();
  });

  it('muestra el error de servidor en el campo Departamento cuando el alta falla con EMPLEADO_DEPARTAMENTO_REQUERIDO_PARA_PUESTO', async () => {
    mswServer.use(
      http.post('*/api/v1/admin/colaboradores', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Error de validación',
            status: 422,
            code: 'EMPLEADO_DEPARTAMENTO_REQUERIDO_PARA_PUESTO',
            errores: [
              {
                campo: 'departamentoId',
                codigo: 'EMPLEADO_DEPARTAMENTO_REQUERIDO_PARA_PUESTO',
                mensaje: 'El puesto está en varios departamentos de la sucursal; indica cuál.',
              },
            ],
          },
          { status: 422 },
        ),
      ),
    );

    render(<EmpleadoInlineForm onCancel={vi.fn()} />, { wrapper: createQueryWrapper() });
    await sincronizar();

    fireEvent.change(screen.getByPlaceholderText('EMP-001'), { target: { value: 'EMP-VEN' } });
    fireEvent.change(screen.getByPlaceholderText('Juana Pérez'), { target: { value: 'Vendedor Mty' } });
    fireEvent.click(screen.getByRole('combobox', { name: /seleccionar sucursal/i }));
    fireEvent.click(await screen.findByText('Monterrey'));
    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    await sincronizar();

    fireEvent.click(screen.getByRole('combobox', { name: /seleccionar departamento/i }));
    fireEvent.click(await screen.findByText('Ventas'));
    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    await sincronizar();

    fireEvent.click(screen.getByRole('combobox', { name: /seleccionar puesto/i }));
    fireEvent.click(await screen.findByText('Vendedor de mostrador'));
    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    await sincronizar();

    // Paso 3: sin acceso (default) — avanzar directo al resumen.
    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));

    // Paso 4: enviar — el backend rechaza por el 422 mockeado arriba.
    fireEvent.click(screen.getByRole('button', { name: /agregar empleado/i }));

    expect(
      await screen.findByText(/el puesto está en varios departamentos de la sucursal/i),
    ).toBeInTheDocument();
  });
});
