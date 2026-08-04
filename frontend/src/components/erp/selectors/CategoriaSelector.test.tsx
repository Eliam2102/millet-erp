import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CategoriaSelector } from '@/components/erp/selectors/CategoriaSelector';
import type { CategoriaArticuloResponse } from '@/modules/catalogos/api';

/**
 * <CategoriaSelector> sobre compartido.categorias_articulo. value/onChange son
 * el id (GUID) real del catálogo. Cold value: como el value es un GUID, el
 * selector muestra el `initialLabel` (nombre del read DTO) hasta que carga.
 */

const GUID = '00000002-0008-0000-0000-000000000005';

function categoria(
  id: string,
  nombre: string,
  activa = true,
): CategoriaArticuloResponse {
  return { id, nombre, estatus: activa ? 0 : 1, version: 1 };
}

describe('<CategoriaSelector>', () => {
  it('cold value: con GUID e initialLabel muestra el NOMBRE, no el GUID', async () => {
    // Catálogo aún vacío (no contiene el value) → el trigger cae al initialLabel.
    mswServer.use(
      http.get('*/api/v1/catalogos/categorias-articulo', () =>
        HttpResponse.json([]),
      ),
    );

    render(
      <CategoriaSelector
        value={GUID}
        onChange={() => {}}
        initialLabel="MAT DE LIMPIEZA"
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(screen.getByText('MAT DE LIMPIEZA')).toBeInTheDocument();
    expect(screen.queryByText(GUID)).not.toBeInTheDocument();
  });

  it('vacío (value null) muestra el placeholder', () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/categorias-articulo', () =>
        HttpResponse.json([]),
      ),
    );

    render(
      <CategoriaSelector value={null} onChange={() => {}} placeholder="Elige categoría" />,
      { wrapper: createQueryWrapper() },
    );

    expect(screen.getByText('Elige categoría')).toBeInTheDocument();
  });

  it('lista solo activas y devuelve el GUID al seleccionar', async () => {
    const onChange = vi.fn();
    mswServer.use(
      http.get('*/api/v1/catalogos/categorias-articulo', () =>
        HttpResponse.json([
          categoria(GUID, 'MAT DE LIMPIEZA', true),
          categoria('00000002-0008-0000-0000-0000000000ff', 'Inactiva', false),
        ]),
      ),
    );

    render(<CategoriaSelector value={null} onChange={onChange} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox'));

    expect(await screen.findByText('MAT DE LIMPIEZA')).toBeInTheDocument();
    expect(screen.queryByText('Inactiva')).not.toBeInTheDocument();

    fireEvent.click(screen.getByText('MAT DE LIMPIEZA'));
    expect(onChange).toHaveBeenCalledWith(GUID);
  });
});
