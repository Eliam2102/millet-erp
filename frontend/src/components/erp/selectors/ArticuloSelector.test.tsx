import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArticuloSelector } from '@/components/erp/selectors/ArticuloSelector';
import { Naturaleza, EstatusCatalogo } from '@/features/compras/api/types';

describe('<ArticuloSelector>', () => {
  it('renderiza placeholder cuando value es null', () => {
    render(<ArticuloSelector value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('Buscar artículo…')).toBeInTheDocument();
  });

  it('aria-expanded=false inicialmente', () => {
    render(<ArticuloSelector value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    const trigger = screen.getByRole('combobox', {
      name: /seleccionar artículo/i,
    });
    expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });

  it('disabled propaga al trigger', () => {
    render(<ArticuloSelector value={null} onChange={() => {}} disabled />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByRole('combobox')).toBeDisabled();
  });

  it('cuando value es un id sin matchear en la lista, muestra el id raw', () => {
    // Mock devuelve lista vacía → seleccionado=undefined → label cae al
    // value raw como fallback (mientras no implementemos useArticulo(id)
    // puntual para preview).
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(
      <ArticuloSelector value="art-orphan-id" onChange={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('art-orphan-id')).toBeInTheDocument();
  });

  it('cuando value matchea un item cargado, muestra clave · nombre', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', () =>
        HttpResponse.json({
          items: [
            {
              id: 'art-1',
              clave: 'TORN-001',
              nombre: 'Tornillo 1/4',
              unidadMedidaDefault: 'PZA',
              naturaleza: Naturaleza.Estandar,
              categoria: null,
              estatus: EstatusCatalogo.Activo,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );

    render(<ArticuloSelector value="art-1" onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    // Esperamos a que el query resuelva y el trigger refleje la label.
    expect(
      await screen.findByText('TORN-001 · Tornillo 1/4'),
    ).toBeInTheDocument();
  });

  it('al seleccionar un artículo dispara onSelect con el item completo (incl. UM) y onChange con el id', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', () =>
        HttpResponse.json({
          items: [
            {
              id: 'art-lt',
              clave: 'SOLV-001',
              nombre: 'Solvente industrial',
              unidadMedidaDefault: 'LT',
              naturaleza: Naturaleza.Estandar,
              categoria: null,
              estatus: EstatusCatalogo.Activo,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );

    const onChange = vi.fn();
    const onSelect = vi.fn();
    render(
      <ArticuloSelector value={null} onChange={onChange} onSelect={onSelect} />,
      { wrapper: createQueryWrapper() },
    );

    // Abrir el popover y esperar a que el item del catálogo aparezca.
    fireEvent.click(screen.getByRole('combobox'));
    const item = await screen.findByText('Solvente industrial');
    fireEvent.click(item);

    expect(onChange).toHaveBeenCalledWith('art-lt');
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'art-lt', unidadMedidaDefault: 'LT' }),
    );
  });

  it('con initialLabel, un id fuera del cap muestra la etiqueta (no el UUID) — REGRESIÓN PRE-EXISTENTE', async () => {
    // La lista capada NO contiene el artículo (simula ranking > tope). Antes
    // del fix esto mostraba el UUID; ahora la etiqueta del DTO enriquecido
    // (initialLabel) lo resuelve (ADR-0042 addendum).
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(
      <ArticuloSelector
        value="art-fuera-de-cap"
        onChange={() => {}}
        initialLabel="ZZZ-999 · Artículo más allá del tope"
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      await screen.findByText('ZZZ-999 · Artículo más allá del tope'),
    ).toBeInTheDocument();
    expect(screen.queryByText('art-fuera-de-cap')).not.toBeInTheDocument();
  });

  it('tras seleccionar, el trigger conserva clave·nombre aunque la lista se recargue vacía', async () => {
    // El endpoint devuelve el item SOLO cuando hay búsqueda por clave; al
    // limpiarse el input tras seleccionar, la recarga (primer page) vuelve
    // vacía → el trigger debe seguir mostrando la etiqueta desde el objeto
    // seleccionado guardado, no caer al UUID.
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        const clave = new URL(request.url).searchParams.get('q');
        const item = {
          id: 'art-x',
          clave: 'SOLV-001',
          nombre: 'Solvente industrial',
          unidadMedidaDefault: 'LT',
          naturaleza: Naturaleza.Estandar,
          categoria: null,
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
      return <ArticuloSelector value={v} onChange={setV} />;
    }
    render(<Controlled />, { wrapper: createQueryWrapper() });

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.change(screen.getByPlaceholderText('Buscar por clave o nombre…'), {
      target: { value: 'SOLV' },
    });
    fireEvent.click(await screen.findByText('Solvente industrial'));

    expect(
      await screen.findByText('SOLV-001 · Solvente industrial'),
    ).toBeInTheDocument();
  });

  // --- Caja única `q` (86e3ufkgd): sustituye las dos cajas de ADR-0045 ---

  it('renderiza una sola caja de búsqueda por clave o nombre', () => {
    render(<ArticuloSelector value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.getAllByRole('textbox')).toHaveLength(1);
    expect(
      screen.getByRole('textbox', { name: 'Buscar artículo' }),
    ).toHaveAttribute('placeholder', 'Buscar por clave o nombre…');
  });

  it.each([
    ['fragmento de nombre', 'papeleria'],
    ['clave', 'TORN'],
  ])('busca por %s: manda ?q= (no clave ni nombre)', async (_n, texto) => {
    let urlVisto = '';
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );
    render(<ArticuloSelector value={null} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.change(screen.getByRole('textbox', { name: 'Buscar artículo' }), {
      target: { value: texto },
    });
    await waitFor(() => expect(urlVisto).toContain(`q=${texto}`));
    expect(urlVisto).not.toContain('clave=');
    expect(urlVisto).not.toContain('nombre=');
    expect(
      await screen.findByText(`Ningún artículo activo coincide con "${texto}".`),
    ).toBeInTheDocument();
  });

  it('con tipoRequisicion, el término viaja junto con el tipo', async () => {
    let urlVisto = '';
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );
    render(
      <ArticuloSelector value={null} onChange={() => {}} tipoRequisicion={0} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.change(screen.getByRole('textbox', { name: 'Buscar artículo' }), {
      target: { value: 'limpieza' },
    });
    await waitFor(() => expect(urlVisto).toContain('q=limpieza'));
    expect(urlVisto).toContain('tipoRequisicion=0');
  });

  it('con tipoRequisicion y sin artículos de ese tipo, lo dice', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/articulos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(
      <ArticuloSelector value={null} onChange={() => {}} tipoRequisicion={2} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('combobox'));
    expect(
      await screen.findByText(
        'No hay artículos dados de alta para este tipo de requisición.',
      ),
    ).toBeInTheDocument();
  });
});
