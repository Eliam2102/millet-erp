import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteSelectorCxc } from './ClienteSelectorCxc';

describe('<ClienteSelectorCxc> · búsqueda por palabras (86e3tfdx8)', () => {
  it('busca en una sola caja con q y entrega el cliente', async () => {
    const consultas: string[] = [];
    mswServer.use(
      http.get('*/api/v1/cuentas-por-cobrar/clientes-lookup', ({ request }) => {
        const url = new URL(request.url);
        consultas.push(url.search);
        return HttpResponse.json(
          url.searchParams.get('q')
            ? [{ id: 'c-1', clave: 'CLI-1', rfc: 'PEGC800101AB1', razonSocial: 'PÉREZ GARCÍA CARLOS' }]
            : [],
        );
      }),
    );
    const onChange = vi.fn();
    render(<ClienteSelectorCxc value={null} onChange={onChange} />, { wrapper: createQueryWrapper() });

    fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar cliente' }));
    fireEvent.change(screen.getByLabelText('Buscar cliente'), { target: { value: 'carlos perez' } });
    fireEvent.click(await screen.findByText('PÉREZ GARCÍA CARLOS'));

    expect(consultas.some((q) => q.includes('q=carlos+perez') && q.includes('limit=50'))).toBe(true);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ id: 'c-1' }));
  });
});
