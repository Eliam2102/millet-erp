import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CanalVentaSelector } from './CanalVentaSelector';

const CANALES = [
  { id: 1, nombre: 'Tienda Cancún' },
  { id: 8, nombre: 'Exportación' },
];

function conCanales(canales: Array<{ id: number; nombre: string }> = CANALES) {
  mswServer.use(
    http.get('*/api/v1/facturacion/catalogos/canales-venta', () =>
      HttpResponse.json(canales),
    ),
  );
}

describe('<CanalVentaSelector> (FAC-ING-PR3)', () => {
  it('llena las opciones desde el lookup del catálogo', async () => {
    conCanales();
    render(<CanalVentaSelector value={1} onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    expect(
      await screen.findByRole('option', { name: 'Tienda Cancún' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('option', { name: 'Exportación' }),
    ).toBeInTheDocument();
    const select = screen.getByRole('combobox', {
      name: /canal de venta/i,
    }) as HTMLSelectElement;
    expect(select.value).toBe('1');
  });

  it('conserva un valor fuera de catálogo como opción "(inactivo)" cuando hay etiqueta', async () => {
    conCanales();
    const onChange = vi.fn();
    render(
      <CanalVentaSelector
        value={9}
        onChange={onChange}
        etiquetaValorActual="Planta Pintura"
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(
      await screen.findByRole('option', { name: /Planta Pintura \(inactivo\)/i }),
    ).toBeInTheDocument();
    // No se normaliza: el documento histórico conserva su canal.
    expect(onChange).not.toHaveBeenCalled();
  });

  it('normaliza al primer canal activo si el default no existe y no hay etiqueta', async () => {
    conCanales([{ id: 4, nombre: 'CC Mérida' }]);
    const onChange = vi.fn();
    render(<CanalVentaSelector value={1} onChange={onChange} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(onChange).toHaveBeenCalledWith(4));
  });
});
