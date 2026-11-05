import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfiguracionIncidencias } from './ConfiguracionIncidencias';
const api = vi.hoisted(() => vi.fn());
vi.mock('@/lib/api', () => ({
  apiRequest: api,
  esApiError: () => false,
  useBodyScopedIdempotencyKey: () => () => 'p20-config',
}));
it('consulta el plazo vigente y guarda cinco días hábiles', async () => {
  api.mockImplementation((_url: string, options?: { method?: string }) =>
    Promise.resolve({
      data: { diasHabilesIncidenciaRecepcion: options?.method === 'PUT' ? 5 : 2 },
    }),
  );
  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <ConfiguracionIncidencias />
    </QueryClientProvider>,
  );
  const input = await screen.findByLabelText('Días hábiles para aclarar diferencias *');
  await waitFor(() => expect(input).toHaveValue(2));
  fireEvent.change(input, { target: { value: '5' } });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar plazo' }));
  await waitFor(() =>
    expect(api).toHaveBeenCalledWith(
      '/api/v1/comercio-exterior/configuracion',
      expect.objectContaining({ method: 'PUT', body: { diasHabilesIncidenciaRecepcion: 5 } }),
    ),
  );
  expect(await screen.findByRole('status')).toHaveTextContent('Plazo guardado');
});
