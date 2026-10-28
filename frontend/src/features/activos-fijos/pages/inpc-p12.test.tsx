import { afterEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { InpcPage } from './InpcPage';
const state = vi.hoisted(() => ({ mutate: vi.fn() }));
vi.mock('../api/hooks', () => ({
  useGuardarActivo: () => ({ mutate: state.mutate, isPending: false }),
}));
vi.mock('../api/libro', () => ({ useLibroQuery: () => ({ data: [] }) }));
afterEach(() => {
  vi.unstubAllGlobals();
  state.mutate.mockReset();
});
it('captura el índice para el mes seleccionado', () => {
  render(<InpcPage />);
  fireEvent.change(screen.getByLabelText('Mes'), { target: { value: '2026-06' } });
  fireEvent.change(screen.getByLabelText('Valor INPC'), { target: { value: '103.123456' } });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar índice' }));
  expect(state.mutate).toHaveBeenCalledWith(
    { ruta: 'inpc', body: { anio: 2026, mes: 6, valor: 103.123456 } },
    expect.anything(),
  );
});
it('conserva el mismo cuerpo multipart en reintentos de una carga', async () => {
  const body = new Blob(['FICTICIO']);
  Object.defineProperty(body, 'arrayBuffer', { value: async () => new ArrayBuffer(1) });
  vi.stubGlobal(
    'Response',
    class {
      async blob() {
        return body;
      }
    },
  );
  vi.stubGlobal('crypto', { subtle: { digest: async () => new Uint8Array([1, 2, 3]).buffer } });
  render(<InpcPage />);
  expect(screen.getByRole('button', { name: 'Cargar Excel' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Archivo Excel'), {
    target: { files: [new File(['FICTICIO'], 'indices.xlsx')] },
  });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cargar Excel' })).toBeEnabled());
  fireEvent.submit(screen.getByRole('button', { name: 'Cargar Excel' }).closest('form')!);
  fireEvent.submit(screen.getByRole('button', { name: 'Cargar Excel' }).closest('form')!);
  expect(state.mutate).toHaveBeenNthCalledWith(1, {
    ruta: 'inpc/excel',
    body,
    archivoHuella: '010203',
  });
  expect(state.mutate.mock.calls[1][0].body).toBe(state.mutate.mock.calls[0][0].body);
});
