import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PuestoSelector } from '@/components/erp/selectors/PuestoSelector';

const SUCURSAL_ID = '00000000-0000-0000-0000-000000000010';
const DEPTO_DIR = '00000000-0000-0000-0000-000000000099';
const DEPTO_VENTAS = '00000000-0000-0000-0000-000000000088';

const PUESTOS_SUCURSAL = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      puestoId: '00000000-0000-0000-0000-000000000001',
      puestoClave: 'GER',
      puestoNombre: 'Gerente General',
      departamentoId: DEPTO_DIR,
      departamentoNombre: 'Dirección',
      estatus: 0,
      version: 1,
    },
    {
      sucursalId: SUCURSAL_ID,
      puestoId: '00000000-0000-0000-0000-000000000002',
      puestoClave: 'VEND',
      puestoNombre: 'Vendedor de Mostrador',
      departamentoId: DEPTO_VENTAS,
      departamentoNombre: 'Ventas',
      estatus: 0,
      version: 1,
    },
  ],
  total: 2,
};

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/puestos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get(
      '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos',
      () => HttpResponse.json(PUESTOS_SUCURSAL),
    ),
  );
});

describe('<PuestoSelector>', () => {
  it('filtra y muestra solo los puestos del departamento seleccionado', async () => {
    const onChange = vi.fn();
    render(
      <PuestoSelector
        value={null}
        onChange={onChange}
        sucursalId={SUCURSAL_ID}
        departamentoId={DEPTO_DIR}
      />,
      { wrapper: createQueryWrapper() },
    );

    // Abrir combobox
    const trigger = screen.getByRole('combobox', { name: /seleccionar puesto/i });
    fireEvent.click(trigger);

    await waitFor(() => {
      // GER pertenece a DEPTO_DIR, debe aparecer
      expect(screen.getByText('Gerente General')).toBeInTheDocument();
    });

    // VEND pertenece a DEPTO_VENTAS, NO debe aparecer
    expect(screen.queryByText('Vendedor de Mostrador')).not.toBeInTheDocument();
  });

  it('muestra una sola vez un puesto asignado a varios departamentos', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/sucursales/:sucursalId/puestos', () =>
        HttpResponse.json({
          items: [
            ...PUESTOS_SUCURSAL.items,
            { ...PUESTOS_SUCURSAL.items[0], departamentoId: DEPTO_VENTAS, departamentoNombre: 'Ventas' },
          ],
          total: 3,
        }),
      ),
    );
    render(
      <PuestoSelector value={null} onChange={vi.fn()} sucursalId={SUCURSAL_ID} />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.click(screen.getByRole('combobox', { name: /seleccionar puesto/i }));

    await waitFor(() => {
      expect(screen.getByText('Vendedor de Mostrador')).toBeInTheDocument();
    });
    expect(screen.getAllByText('Gerente General')).toHaveLength(1);
  });

  it('muestra mensaje descriptivo cuando no hay puestos en el departamento seleccionado', async () => {
    const DEPTO_VACIO = '00000000-0000-0000-0000-000000000077';
    render(
      <PuestoSelector
        value={null}
        onChange={vi.fn()}
        sucursalId={SUCURSAL_ID}
        departamentoId={DEPTO_VACIO}
      />,
      { wrapper: createQueryWrapper() },
    );

    const trigger = screen.getByRole('combobox', { name: /seleccionar puesto/i });
    fireEvent.click(trigger);

    await waitFor(() => {
      expect(
        screen.getByText(/no hay puestos asignados a este departamento/i),
      ).toBeInTheDocument();
    });
  });
});
