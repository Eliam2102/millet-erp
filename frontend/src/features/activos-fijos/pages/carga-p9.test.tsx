import { beforeEach, afterEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CargaInicialPage } from './CargaInicialPage';
const state = vi.hoisted(() => ({ request: vi.fn(), form: undefined as FormData | undefined }));
vi.mock('@/lib/api', () => ({ apiRequest: state.request }));
vi.mock('../api/hooks', () => ({
  AF_BASE: '/api/v1/activos-fijos',
  afKeys: { all: ['af'] },
  useClasesActivo: () => ({ data: [] }),
}));
vi.mock('../components/TablaLibro', () => ({
  TablaLibro: ({ titulo, filas }: { titulo: string; filas: unknown[] }) => (
    <section aria-label={titulo}>{JSON.stringify(filas)}</section>
  ),
}));
beforeEach(() => {
  state.request.mockReset();
  state.form = undefined;
  vi.stubGlobal(
    'Response',
    class {
      constructor(form: FormData) {
        state.form = form;
      }
      async blob() {
        return new Blob(['multipart'], { type: 'multipart/form-data; boundary=test' });
      }
    },
  );
});
afterEach(() => vi.unstubAllGlobals());
function renderPage() {
  render(
    <QueryClientProvider
      client={
        new QueryClient({
          defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
        })
      }
    >
      <CargaInicialPage />
    </QueryClientProvider>,
  );
}
it('envía Excel y hoja, muestra avisos, confirma sólo una previa vigente', async () => {
  state.request.mockResolvedValue({
    data: {
      valida: true,
      errores: [],
      cuadre: [],
      avisos: [{ renglon: 4, columna: 'responsable', mensaje: 'Sin responsable' }],
    },
  });
  renderPage();
  const input = screen.getByLabelText('Archivo Excel o CSV *');
  expect(input).toHaveAttribute('accept', '.xlsx,.csv');
  expect(screen.getByRole('button', { name: 'Descargar plantilla Excel' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Confirmar carga' })).toBeDisabled();
  const file = new File(['excel'], 'sap.xlsx');
  fireEvent.change(input, { target: { files: [file] } });
  fireEvent.change(screen.getByLabelText(/Nombre de hoja/), { target: { value: 'SAP' } });
  fireEvent.click(screen.getByRole('button', { name: 'Prueba en seco' }));
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Confirmar carga' })).toBeEnabled(),
  );
  expect(state.form?.get('archivo')).toBe(file);
  expect(state.form?.get('hoja')).toBe('SAP');
  expect(state.form?.get('corte')).toBe('2026-10-31');
  expect(screen.getByRole('region', { name: 'Avisos' })).toHaveTextContent('Sin responsable');
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar carga' }));
  await waitFor(() => expect(state.request).toHaveBeenCalledTimes(2));
  expect(state.request.mock.calls[1][0]).toContain('/confirmar');
  expect(state.request.mock.calls[1][1].idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);
  await waitFor(() => expect(screen.getByLabelText(/Nombre de hoja/)).toBeEnabled());
  fireEvent.change(screen.getByLabelText(/Nombre de hoja/), { target: { value: 'Otra' } });
  expect(screen.getByRole('button', { name: 'Confirmar carga' })).toBeDisabled();
});
it('rechaza extensiones incorrectas y muestra fila, columna y motivo de errores', async () => {
  renderPage();
  const input = screen.getByLabelText('Archivo Excel o CSV *');
  fireEvent.change(input, { target: { files: [new File(['x'], 'sap.xls')] } });
  expect(screen.getByRole('alert')).toHaveTextContent('hasta 10 MB');
  expect(screen.getByRole('button', { name: 'Prueba en seco' })).toBeDisabled();
  state.request.mockResolvedValue({
    data: {
      valida: false,
      errores: [{ renglon: 4, columna: 'clase', mensaje: 'La clase no existe' }],
      cuadre: [],
    },
  });
  fireEvent.change(input, { target: { files: [new File(['csv'], 'sap.csv')] } });
  fireEvent.click(screen.getByRole('button', { name: 'Prueba en seco' }));
  await waitFor(() =>
    expect(screen.getByRole('region', { name: 'Errores por renglón' })).toHaveTextContent(
      'La clase no existe',
    ),
  );
  expect(screen.getByRole('region', { name: 'Errores por renglón' })).toHaveTextContent(
    '"renglon":4,"columna":"clase"',
  );
  expect(screen.getByRole('button', { name: 'Confirmar carga' })).toBeDisabled();
});
