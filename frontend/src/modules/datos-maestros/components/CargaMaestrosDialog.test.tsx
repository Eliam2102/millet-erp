import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CargaMaestrosDialog, type ResultadoCargaMaestros } from './CargaMaestrosDialog';
const mocks = vi.hoisted(() => ({ permiso: true, api: vi.fn() }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => mocks.permiso }));
vi.mock('@/lib/api', () => ({ apiRequest: mocks.api }));
const previa: ResultadoCargaMaestros = {
  huella: 'HUELLA', confirmado: false, nuevos: 1, actualiza: 0, sinCambio: 0, errores: 1,
  renglones: [
    { renglon: 2, estado: 'Nuevo', valores: { Clave: 'FICTICIO' }, cambios: [{ campo: 'Nombre', antes: null, despues: 'Artículo de prueba' }], errores: [] },
    { renglon: 3, estado: 'Error', valores: { Clave: 'ERROR' }, cambios: [], errores: [{ renglon: 3, columna: 'Nombre', codigo: 'REQUERIDO', mensaje: 'Capture el nombre.' }] },
  ],
};
function abrir() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={qc}><CargaMaestrosDialog catalogo="articulos" /></QueryClientProvider>);
  fireEvent.click(screen.getByRole('button', { name: 'Cargar desde Excel' }));
}
function elegir() {
  fireEvent.change(screen.getByLabelText('Archivo Excel o CSV *'), { target: { files: [new File(['Clave,Nombre\nFICTICIO,Prueba'], 'prueba.csv', { type: 'text/csv' })] } });
}
beforeEach(() => { mocks.permiso = true; mocks.api.mockReset(); });
describe('Carga de maestros', () => {
  it('oculta la carga sin permiso', () => {
    mocks.permiso = false; render(<CargaMaestrosDialog catalogo="clientes" />);
    expect(screen.queryByRole('button', { name: 'Cargar desde Excel' })).not.toBeInTheDocument();
  });
  it('exige archivo y rechaza extensiones incorrectas', () => {
    abrir(); expect(screen.getByRole('button', { name: 'Ver vista previa' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Archivo Excel o CSV *'), { target: { files: [new File(['dato'], 'prueba.exe')] } });
    expect(screen.getByRole('alert')).toHaveTextContent('hasta 10 MB'); expect(mocks.api).not.toHaveBeenCalled();
  });
  it('muestra la previa y confirma con huella y llave sólo por acción explícita', async () => {
    const campos = vi.spyOn(FormData.prototype, 'set');
    mocks.api.mockResolvedValueOnce({ data: previa }).mockResolvedValueOnce({ data: { ...previa, confirmado: true } });
    abrir(); elegir(); fireEvent.click(screen.getByRole('button', { name: 'Ver vista previa' }));
    await screen.findByText('Paso 2 de 3', { exact: false });
    expect(mocks.api).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Nombre: Capture el nombre.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar errores' })).toBeInTheDocument();
    expect(screen.getByText('Nombre: Sin valor → Artículo de prueba')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar carga' }));
    // Serializar el FormData a blob para la llave de idempotencia tarda más en el runner de CI.
    await screen.findByText('Paso 3 de 3', { exact: false }, { timeout: 10_000 });
    const [ruta, opciones] = mocks.api.mock.calls[1];
    expect(ruta).toContain('/confirmar'); expect(campos).toHaveBeenCalledWith('huella', 'HUELLA'); expect(opciones.body.size).toBeGreaterThan(0); campos.mockRestore(); expect(opciones.idempotencyKey).toBeTruthy();
  });
  it('deshabilita confirmar si ningún renglón cambia', async () => {
    mocks.api.mockResolvedValue({ data: { ...previa, nuevos: 0, actualiza: 0 } });
    abrir(); elegir(); fireEvent.click(screen.getByRole('button', { name: 'Ver vista previa' }));
    await screen.findByText('Paso 2 de 3', { exact: false });
    expect(screen.getByRole('button', { name: 'Confirmar carga' })).toBeDisabled();
  });
  it('conserva la pantalla y explica el error del servidor', async () => {
    mocks.api.mockRejectedValue(new Error('El archivo cambió. Genere otra vista previa.'));
    abrir(); elegir(); fireEvent.click(screen.getByRole('button', { name: 'Ver vista previa' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('El archivo cambió'));
    expect(screen.getByRole('button', { name: 'Ver vista previa' })).toBeEnabled();
  });
});
