import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorSelector } from '@/components/erp/selectors/ProveedorSelector';
import {
  EstatusCatalogo,
} from '@/features/compras/api/types';
import { TipoPersonaProveedor } from '@/features/catalogos/api/types';

describe('<ProveedorSelector>', () => {
  it('renderiza placeholder con value null', () => {
    render(<ProveedorSelector value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('Buscar proveedor…')).toBeInTheDocument();
  });

  it('cuando value matchea, muestra clave · razonSocial en trigger', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', () =>
        HttpResponse.json({
          items: [
            {
              id: 'prov-1',
              clave: 'PROV-001',
              razonSocial: 'Aceros del Norte SA',
              nombreComercial: null,
              rfc: 'AND900101AB1',
              tipoPersona: TipoPersonaProveedor.Moral,
              condicionesPagoDias: 30,
              monedaPreferidaId: null,
              estatus: EstatusCatalogo.Activo,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );
    render(<ProveedorSelector value="prov-1" onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(
      await screen.findByText('PROV-001 · Aceros del Norte SA'),
    ).toBeInTheDocument();
  });

  it('con initialLabel, un id fuera del cap muestra la etiqueta (no el UUID) — REGRESIÓN PRE-EXISTENTE', async () => {
    // La lista capada NO contiene el proveedor (simula ranking > tope). Antes
    // del fix esto mostraba el UUID; ahora la etiqueta del DTO enriquecido
    // (initialLabel) lo resuelve (ADR-0042 addendum).
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(
      <ProveedorSelector
        value="prov-fuera-de-cap"
        onChange={() => {}}
        initialLabel="ZZZ-999 · Proveedor más allá del tope SA"
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      await screen.findByText('ZZZ-999 · Proveedor más allá del tope SA'),
    ).toBeInTheDocument();
    expect(screen.queryByText('prov-fuera-de-cap')).not.toBeInTheDocument();
  });

  it('tras seleccionar, el trigger conserva clave·razonSocial aunque la lista se recargue vacía', async () => {
    // El endpoint devuelve el item SOLO cuando hay búsqueda por clave; al
    // limpiarse el input tras seleccionar, la recarga (primer page) vuelve
    // vacía → el trigger debe seguir mostrando la etiqueta desde el objeto
    // seleccionado guardado, no caer al UUID.
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', ({ request }) => {
        const clave = new URL(request.url).searchParams.get('clave');
        const item = {
          id: 'prov-x',
          clave: 'PROV-777',
          razonSocial: 'Distribuidora Lejana SA',
          nombreComercial: null,
          rfc: 'DLE900101AB1',
          tipoPersona: TipoPersonaProveedor.Moral,
          condicionesPagoDias: 30,
          monedaPreferidaId: null,
          estatus: EstatusCatalogo.Activo,
        };
        return HttpResponse.json({
          items: clave ? [item] : [],
          offset: 0,
          limit: 50,
          total: clave ? 1 : 0,
        });
      }),
    );

    function Controlled() {
      const [v, setV] = useState<string | null>(null);
      return <ProveedorSelector value={v} onChange={setV} />;
    }
    render(<Controlled />, { wrapper: createQueryWrapper() });

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.change(screen.getByPlaceholderText('Buscar por clave…'), {
      target: { value: 'PROV' },
    });
    fireEvent.click(await screen.findByText('Distribuidora Lejana SA'));

    expect(
      await screen.findByText('PROV-777 · Distribuidora Lejana SA'),
    ).toBeInTheDocument();
  });

  it('resuelve un value frío (sin selected ni initialLabel) vía lookup por id — no muestra el GUID', async () => {
    // Caso "recargar con ?proveedorId= en la URL": el id no está en la página
    // del typeahead y no se pasó initialLabel. El selector lo resuelve con
    // useProveedor(id) → GET /proveedores/{id} (lookup puntual, cacheado).
    const COLD_ID = '00000005-0001-0000-0000-000000000099';
    mswServer.use(
      // Typeahead inicial vacío → el value no está en la página.
      http.get('*/api/v1/catalogos/proveedores', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
      // Lookup puntual por id (detalle).
      http.get('*/api/v1/catalogos/proveedores/:id', ({ params }) =>
        HttpResponse.json({
          id: params.id,
          clave: 'PRV-COLD-001',
          claveLegacy: null,
          razonSocial: 'Proveedor Frío SA',
          nombreComercial: null,
          rfc: 'PFR900101AB1',
          tipoPersona: TipoPersonaProveedor.Moral,
          condicionesPagoDias: null,
          monedaPreferidaId: null,
          email: null,
          telefono: null,
          estatus: EstatusCatalogo.Activo,
        }),
      ),
    );

    render(<ProveedorSelector value={COLD_ID} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    // El trigger muestra clave · razón social resueltos por el lookup, no el GUID.
    expect(
      await screen.findByText('PRV-COLD-001 · Proveedor Frío SA'),
    ).toBeInTheDocument();
    expect(screen.queryByText(COLD_ID)).not.toBeInTheDocument();
  });

  it('busca por nombre: teclear en la caja de nombre consulta con ?nombre y renderiza', async () => {
    // El endpoint devuelve el item SOLO cuando la búsqueda viaja por nombre
    // (no por clave) — comprueba que el selector manda `nombre` (Defect A).
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', ({ request }) => {
        const url = new URL(request.url);
        const nombre = url.searchParams.get('nombre');
        const clave = url.searchParams.get('clave');
        const item = {
          id: 'prov-n',
          clave: 'PROV-555',
          razonSocial: 'Aceros del Norte SA',
          nombreComercial: 'AcerNorte',
          rfc: 'AND900101AB1',
          tipoPersona: TipoPersonaProveedor.Moral,
          condicionesPagoDias: 30,
          monedaPreferidaId: null,
          estatus: EstatusCatalogo.Activo,
        };
        const aplica = Boolean(nombre) && !clave;
        return HttpResponse.json({
          items: aplica ? [item] : [],
          offset: 0,
          limit: 50,
          total: aplica ? 1 : 0,
        });
      }),
    );

    render(<ProveedorSelector value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.change(screen.getByPlaceholderText('Buscar por nombre…'), {
      target: { value: 'aceros' },
    });

    expect(await screen.findByText('Aceros del Norte SA')).toBeInTheDocument();
  });

  it('exclusividad: clave y nombre se limpian/deshabilitan mutuamente', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/proveedores', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );

    render(<ProveedorSelector value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox'));
    const cajaClave = screen.getByPlaceholderText(
      'Buscar por clave…',
    ) as HTMLInputElement;
    const cajaNombre = screen.getByPlaceholderText(
      'Buscar por nombre…',
    ) as HTMLInputElement;

    // Texto en clave → la caja de nombre se deshabilita.
    fireEvent.change(cajaClave, { target: { value: 'PROV' } });
    expect(cajaClave.value).toBe('PROV');
    expect(cajaNombre).toBeDisabled();

    // Limpiar clave re-habilita nombre; escribir en nombre limpia clave.
    fireEvent.change(cajaClave, { target: { value: '' } });
    expect(cajaNombre).not.toBeDisabled();
    fireEvent.change(cajaNombre, { target: { value: 'acer' } });
    expect(cajaNombre.value).toBe('acer');
    expect(cajaClave.value).toBe('');
    expect(cajaClave).toBeDisabled();
  });
});
