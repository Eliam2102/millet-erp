import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SheetDepartamentosDeSucursal } from '@/modules/administracion/components/SheetDepartamentosDeSucursal';

const sucursal = {
  id: 's-1',
  clave: 'MID',
  nombre: 'Planta Conkal',
  estatus: 0,
  version: 1,
};

const CATALOGO_GLOBAL_URL = '*/api/v1/catalogos/departamentos';
const ASIGNACIONES_URL =
  '*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos';

describe('<SheetDepartamentosDeSucursal> — smoke', () => {
  it('sucursal === null → cerrado (sin contenido)', () => {
    render(
      <SheetDepartamentosDeSucursal
        sucursal={null}
        onOpenChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.queryByText(/Departamentos en/i)).not.toBeInTheDocument();
  });

  it('abierto → marca asignados Activa, no asignados Sin asignar', async () => {
    mswServer.use(
      http.get(CATALOGO_GLOBAL_URL, () =>
        HttpResponse.json({
          items: [
            { id: 'd-1', clave: 'COMPRAS', nombre: 'Compras', estatus: 0 },
            { id: 'd-2', clave: 'SIS', nombre: 'SISTEMAS', estatus: 0 },
          ],
          offset: 0,
          limit: 200,
          total: 2,
        }),
      ),
      http.get(ASIGNACIONES_URL, () =>
        HttpResponse.json({
          items: [
            {
              sucursalId: 's-1',
              departamentoId: 'd-1',
              departamentoClave: 'COMPRAS',
              departamentoNombre: 'Compras',
              estatus: 0,
              version: 1,
            },
          ],
          total: 1,
        }),
      ),
    );

    render(
      <SheetDepartamentosDeSucursal
        sucursal={sucursal}
        onOpenChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      await screen.findByText(/Departamentos en MID/i),
    ).toBeInTheDocument();
    // Asignado COMPRAS muestra badge "Activa"
    expect(await screen.findByText('Activa')).toBeInTheDocument();
    // No asignado SIS muestra "Sin asignar"
    expect(screen.getByText('Sin asignar')).toBeInTheDocument();
    // Acción Asignar visible para SIS
    expect(
      screen.getByRole('button', { name: /asignar departamento sis/i }),
    ).toBeInTheDocument();
    // Acción Desactivar visible para COMPRAS activo
    expect(
      screen.getByRole('button', { name: /desactivar departamento compras/i }),
    ).toBeInTheDocument();
  });

  it('error de carga → muestra mensaje de error', async () => {
    mswServer.use(
      http.get(CATALOGO_GLOBAL_URL, () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
      http.get(ASIGNACIONES_URL, () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    );

    render(
      <SheetDepartamentosDeSucursal
        sucursal={sucursal}
        onOpenChange={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      await screen.findByText(/no se pudo cargar el catálogo/i),
    ).toBeInTheDocument();
  });
});
