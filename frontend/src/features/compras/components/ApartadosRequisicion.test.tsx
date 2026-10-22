import { afterEach, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ApartadosRequisicion } from './ApartadosRequisicion';

const props = {
  requisicionId: 'DEMO-rq',
  resolverPartida: () => 'Partida DEMO',
  puedeLiberarPorEstado: true,
};
afterEach(() => useAuthStore.setState({ permisos: [] }));
function preparar(permiso: boolean) {
  useAuthStore.setState({ permisos: permiso ? ['compras.requisiciones.liberar-apartado'] : [] });
  mswServer.use(
    http.get('*/api/v1/compras/requisiciones/DEMO-rq/apartados', () =>
      HttpResponse.json([{ lineaId: 'DEMO-partida', pendiente: 5, unidadMedida: 'PZA' }]),
    ),
  );
}
it('oculta liberar apartado sin permiso', async () => {
  preparar(false);
  render(<ApartadosRequisicion {...props} />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('Partida DEMO')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Liberar apartado' })).not.toBeInTheDocument();
});
it('exige motivo y envía cantidad parcial con idempotencia', async () => {
  preparar(true);
  let recibido: unknown;
  let clave: string | null = null;
  mswServer.use(
    http.post(
      '*/api/v1/compras/requisiciones/DEMO-rq/apartados/DEMO-partida/liberar',
      async ({ request }) => {
        recibido = await request.json();
        clave = request.headers.get('Idempotency-Key');
        return new HttpResponse(null, { status: 204 });
      },
    ),
  );
  render(<ApartadosRequisicion {...props} />, { wrapper: createQueryWrapper() });
  fireEvent.click(await screen.findByRole('button', { name: 'Liberar apartado' }));
  const confirmar = screen.getByRole('button', { name: 'Confirmar liberación' });
  expect(confirmar).toBeDisabled();
  fireEvent.change(screen.getByLabelText(/cantidad a liberar/i), { target: { value: '2' } });
  fireEvent.change(screen.getByLabelText(/motivo/i), {
    target: { value: 'DEMO cambio de prioridad' },
  });
  fireEvent.click(confirmar);
  await waitFor(() =>
    expect(recibido).toEqual({ cantidad: 2, motivo: 'DEMO cambio de prioridad' }),
  );
  expect(clave).toBeTruthy();
});
