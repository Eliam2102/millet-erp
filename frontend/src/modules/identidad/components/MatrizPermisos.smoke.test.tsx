import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MatrizPermisos } from '@/modules/identidad/components/MatrizPermisos';

const respuestaAgrupada = {
  items: [],
  grupos: [
    {
      modulo: 'identidad',
      items: [
        {
          id: 'p-id-1',
          codigo: 'identidad.roles.leer',
          modulo: 'identidad',
          recurso: 'roles',
          accion: 'leer',
          descripcion: 'Lee la lista de roles.',
        },
        {
          id: 'p-id-2',
          codigo: 'identidad.roles.crear',
          modulo: 'identidad',
          recurso: 'roles',
          accion: 'crear',
          descripcion: 'Crea un rol nuevo.',
        },
      ],
    },
    {
      modulo: 'compras',
      items: [
        {
          id: 'p-cmp-1',
          codigo: 'compras.requisiciones.leer',
          modulo: 'compras',
          recurso: 'requisiciones',
          accion: 'leer',
          descripcion: 'Lee requisiciones.',
        },
      ],
    },
  ],
};

function mockGetPermisos() {
  mswServer.use(
    http.get('*/api/v1/identidad/permisos', () =>
      HttpResponse.json(respuestaAgrupada),
    ),
  );
}

describe('<MatrizPermisos> — smoke', () => {
  it('renderiza un Collapsible por módulo con conteo seleccionados/total', async () => {
    mockGetPermisos();

    render(
      <MatrizPermisos
        rolId="r-1"
        permisoIdsIniciales={['p-id-1']}
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText(/identidad/i)).toBeInTheDocument(),
    );

    // Conteo "1/2" para identidad y "0/1" para compras.
    expect(screen.getByText('1/2')).toBeInTheDocument();
    expect(screen.getByText('0/1')).toBeInTheDocument();
  });

  it('selección parcial deja el checkbox del módulo en estado indeterminado', async () => {
    mockGetPermisos();

    render(
      <MatrizPermisos rolId="r-1" permisoIdsIniciales={['p-id-1']} />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(
        screen.getByLabelText(/seleccionar todos los permisos de identidad/i),
      ).toBeInTheDocument(),
    );

    // identidad tiene 1/2 → indeterminado; compras 0/1 → desmarcado.
    expect(
      screen.getByLabelText(/seleccionar todos los permisos de identidad/i),
    ).toHaveAttribute('data-state', 'indeterminate');
    expect(
      screen.getByLabelText(/seleccionar todos los permisos de compras/i),
    ).toHaveAttribute('data-state', 'unchecked');
  });

  it('expandir un módulo muestra los permisos hijos', async () => {
    mockGetPermisos();

    render(
      <MatrizPermisos rolId="r-1" permisoIdsIniciales={[]} />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /permisos de identidad/i }),
      ).toBeInTheDocument(),
    );

    // Antes de expandir, los items hijos no son visibles.
    expect(screen.queryByText('identidad.roles.leer')).toBeNull();

    fireEvent.click(
      screen.getByRole('button', { name: /permisos de identidad/i }),
    );

    await waitFor(() =>
      expect(screen.getByText('identidad.roles.leer')).toBeInTheDocument(),
    );
    expect(screen.getByText('identidad.roles.crear')).toBeInTheDocument();
  });

  it('marca todos los hijos al click en el checkbox del módulo (propagación)', async () => {
    mockGetPermisos();

    render(
      <MatrizPermisos rolId="r-1" permisoIdsIniciales={[]} />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(
        screen.getByLabelText(/seleccionar todos los permisos de identidad/i),
      ).toBeInTheDocument(),
    );

    // Conteo inicial 0/2.
    expect(screen.getByText('0/2')).toBeInTheDocument();

    fireEvent.click(
      screen.getByLabelText(/seleccionar todos los permisos de identidad/i),
    );

    await waitFor(() =>
      expect(screen.getByText('2/2')).toBeInTheDocument(),
    );

    // El botón Guardar cambios debe estar habilitado tras el toggle.
    const guardar = screen.getByRole('button', { name: /guardar cambios/i });
    expect(guardar).not.toBeDisabled();
  });

  it('disabled=true muestra banner y no renderiza botón Guardar', async () => {
    mockGetPermisos();

    render(
      <MatrizPermisos
        rolId="r-sys"
        permisoIdsIniciales={['p-id-1']}
        disabled
        disabledHint="Rol del sistema"
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText(/rol del sistema/i)).toBeInTheDocument(),
    );

    expect(
      screen.queryByRole('button', { name: /guardar cambios/i }),
    ).toBeNull();
  });
});
