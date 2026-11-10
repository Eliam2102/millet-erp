import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaTamborSelect } from '@/features/almacen/components/LineaTamborSelect';

/** M-172: la línea destino solo se pide para artículos marcados «Se consume por tambor a línea». */
function articulo(consumoPorTamborALinea: boolean) {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos/a-1', () => HttpResponse.json({ id: 'a-1', clave: 'SIL-A', consumoPorTamborALinea })),
    http.get('*/api/v1/almacen/tambores-linea', () =>
      HttpResponse.json([{ id: 'l-1', sucursalId: 's-1', clave: 'INS1', nombre: 'Insulado 1', proceso: 'INSULADO', tambores: [] }]),
    ),
  );
}

describe('<LineaTamborSelect>', () => {
  it('artículo marcado: pide la línea de producción', async () => {
    articulo(true);
    render(<LineaTamborSelect articuloId="a-1" value={null} onChange={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('combobox', { name: 'Línea de producción destino' })).toBeInTheDocument();
    expect(screen.getByText(/piezas enteras/)).toBeInTheDocument();
  });

  it('artículo sin marca: no muestra nada', async () => {
    articulo(false);
    const { container } = render(<LineaTamborSelect articuloId="a-1" value={null} onChange={vi.fn()} />, {
      wrapper: createQueryWrapper(),
    });
    await new Promise((r) => setTimeout(r, 50));
    expect(container).toBeEmptyDOMElement();
  });
});
