import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DepartamentoSelectorPorSucursal } from '@/components/erp/selectors/DepartamentoSelectorPorSucursal';

const ENDPOINT = '*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos';

describe('<DepartamentoSelectorPorSucursal>', () => {
  it('sucursalId null → disabled con hint, NO dispara request', async () => {
    let urlVisto = false;
    mswServer.use(
      http.get(ENDPOINT, () => {
        urlVisto = true;
        return HttpResponse.json({ items: [], total: 0 });
      }),
    );

    render(
      <DepartamentoSelectorPorSucursal
        sucursalId={null}
        value={null}
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      screen.getByText('Selecciona primero una sucursal'),
    ).toBeInTheDocument();
    // El trigger combobox queda disabled.
    expect(
      screen.getByRole('combobox', {
        name: /seleccionar departamento de la sucursal/i,
      }),
    ).toBeDisabled();
    // Espera un tick — sin sucursal NO debe dispararse la query.
    await new Promise((r) => setTimeout(r, 10));
    expect(urlVisto).toBe(false);
  });

  it('con sucursalId → muestra activos, filtra Inactivos', async () => {
    mswServer.use(
      http.get(ENDPOINT, () =>
        HttpResponse.json({
          items: [
            {
              sucursalId: 's-1',
              departamentoId: 'd-act',
              departamentoClave: 'COMPRAS',
              departamentoNombre: 'Compras y Adquisiciones',
              estatus: 0, // Activo
              version: 1,
            },
            {
              sucursalId: 's-1',
              departamentoId: 'd-ina',
              departamentoClave: 'CAL',
              departamentoNombre: 'Calidad',
              estatus: 1, // Inactivo — debe filtrarse
              version: 1,
            },
          ],
          total: 2,
        }),
      ),
    );

    render(
      <DepartamentoSelectorPorSucursal
        sucursalId="s-1"
        value="d-act"
        onChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      await screen.findByText('COMPRAS · Compras y Adquisiciones'),
    ).toBeInTheDocument();
  });
});
