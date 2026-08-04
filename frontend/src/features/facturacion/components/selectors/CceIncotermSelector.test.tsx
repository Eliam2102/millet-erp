import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CceIncotermSelector } from '@/features/facturacion/components/selectors/CceIncotermSelector';

/**
 * <CceIncotermSelector/> — reusa el catálogo local de Incoterms pero su
 * value/onChange es el CÓDIGO SAT (FOB), no el id GUID. Este test blinda
 * justo ese remapeo: seleccionar entrega el código, y un value = código
 * resuelve la etiqueta sin caer al GUID.
 */
describe('<CceIncotermSelector>', () => {
  const incoterms = [
    { id: '11111111-1111-4111-8111-111111111111', codigo: 'FOB', nombre: 'Free On Board', estatus: 0 },
    { id: '22222222-2222-4222-8222-222222222222', codigo: 'CIF', nombre: 'Cost Insurance and Freight', estatus: 0 },
  ];

  function mockCatalogo() {
    mswServer.use(
      http.get('*/api/v1/catalogos/incoterms', () => HttpResponse.json(incoterms)),
    );
  }

  it('onChange entrega el CÓDIGO SAT, no el id GUID', async () => {
    mockCatalogo();
    const onChange = vi.fn();

    render(<CceIncotermSelector value={null} onChange={onChange} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByText('Free On Board'));

    expect(onChange).toHaveBeenCalledWith('FOB');
  });

  it('resuelve la etiqueta desde el código guardado', async () => {
    mockCatalogo();

    render(<CceIncotermSelector value="CIF" onChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    // El trigger muestra "código · nombre", no el GUID ni el código crudo.
    expect(
      await screen.findByText('CIF · Cost Insurance and Freight'),
    ).toBeInTheDocument();
  });
});
