import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { InformacionLogisticaForm } from './InformacionLogisticaForm';
import { EstadoOrdenCompra, type OrdenCompraDetalleResponse } from '../api/types';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const mock = vi.hoisted(() => ({ mutate: vi.fn(), catalogoError: null as Error | null, conkal: 'Calle de prueba 1, Conkal' }));
vi.mock('@/features/catalogos/api', () => ({
  useTransportistas: () => ({ data: [] }),
  useSucursales: () => ({
    data: mock.catalogoError ? undefined : { items: [
      { id: 'conkal', clave: 'CON', nombre: 'Conkal', estatus: 0, direccionEntrega: mock.conkal },
      { id: 'circuito', clave: 'CIR', nombre: 'Circuito', estatus: 0, direccionEntrega: 'Calle de prueba 2, Mérida' },
      { id: 'chichi', clave: 'CHI', nombre: 'Chichí Suárez', estatus: 0, direccionEntrega: 'Calle de prueba 3, Mérida' },
      { id: 'cancun', clave: 'CAN', nombre: 'Cancún', estatus: 0, direccionEntrega: 'Calle de prueba 4, Cancún' },
      { id: 'inactiva', clave: 'INA', nombre: 'Inactiva', estatus: 1, direccionEntrega: 'No elegir' },
      { id: 'sin-direccion', clave: 'SIN', nombre: 'Sin domicilio', estatus: 0, direccionEntrega: null },
    ] },
    error: mock.catalogoError, isLoading: false,
  }),
}));
vi.mock('@/features/compras/ordenes/api/useActualizarInformacionLogistica', () => ({
  useActualizarInformacionLogistica: () => ({ mutate: mock.mutate, isPending: false }),
}));
vi.mock('@/components/erp/collaboration/conflict-dialog-context', () => ({
  useConflictDialog: () => ({ openSimple: vi.fn() }),
}));
vi.mock('@tanstack/react-query', async orig => ({
  ...(await orig<typeof import('@tanstack/react-query')>()), useQueryClient: () => ({}),
}));
vi.mock('@/components/erp/selectors/TransportistaSelector', () => ({
  TransportistaSelector: () => <span>Catálogo de transportistas</span>,
}));

function oc(direccion: string | null = null) {
  return {
    id: 'oc-1', sucursalDestinoId: 'inventario-destino', estado: EstadoOrdenCompra.Borrador,
    infoLogisticaDireccion: direccion, infoLogisticaTransportistaId: null,
    infoLogisticaTransportistaTexto: 'Transportista de prueba', infoLogisticaNumeroGuia: 'GUIA-1',
    infoLogisticaInstrucciones: 'Entregar en recepción',
  } as OrdenCompraDetalleResponse;
}
function editar(direccion: string | null = null) {
  const view = render(<InformacionLogisticaForm oc={oc(direccion)} />);
  fireEvent.click(screen.getByRole('button', { name: 'Editar' }));
  return view;
}
async function elegir(nombre: RegExp) {
  fireEvent.click(screen.getByRole('combobox', { name: 'Sede de entrega' }));
  fireEvent.click(await screen.findByRole('option', { name: nombre }));
}
beforeEach(() => {
  mock.mutate.mockReset(); mock.catalogoError = null;
  mock.conkal = 'Calle de prueba 1, Conkal';
  useAuthStore.setState({ permisos: [PermisosCanonicos.ComprasOrdenesLogistica] } as never);
});

describe('Dirección de entrega de OC', () => {
  it.each([
    [/Conkal/, 'Calle de prueba 1, Conkal'], [/Circuito/, 'Calle de prueba 2, Mérida'],
    [/Chichí Suárez/, 'Calle de prueba 3, Mérida'], [/Cancún/, 'Calle de prueba 4, Cancún'],
  ] as const)('copia domicilio de %s sin cambiar el destino de inventario ni los otros datos', async (nombre, direccion) => {
    editar(); await elegir(nombre);
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(mock.mutate).toHaveBeenCalled());
    const args = mock.mutate.mock.calls[0][0];
    expect(args.command).toEqual({ direccionEntrega: direccion, transportistaId: null,
      transportistaTexto: 'Transportista de prueba', numeroGuia: 'GUIA-1', instruccionesEnvio: 'Entregar en recepción' });
    expect(args.command).not.toHaveProperty('sucursalDestinoId');
    expect(args.idempotencyKey).toBeTruthy();
  });

  it('conserva la dirección histórica personalizada al abrir y guardar', async () => {
    editar('Bodega histórica, dirección de prueba');
    expect(screen.getByRole('textbox', { name: 'Dirección de entrega' })).toHaveValue('Bodega histórica, dirección de prueba');
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(mock.mutate).toHaveBeenCalled());
    expect(mock.mutate.mock.calls[0][0].command.direccionEntrega).toBe('Bodega histórica, dirección de prueba');
  });

  it('no actualiza el snapshot cuando cambia el catálogo', async () => {
    const anterior = mock.conkal;
    const view = editar(anterior);
    mock.conkal = 'Domicilio nuevo de prueba';
    view.rerender(<InformacionLogisticaForm oc={oc(anterior)} />);
    expect(screen.getByRole('textbox', { name: 'Dirección de entrega' })).toHaveValue(anterior);
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(mock.mutate).toHaveBeenCalled());
    expect(mock.mutate.mock.calls[0][0].command.direccionEntrega).toBe(anterior);
  });

  it('Otra explícita requiere texto y permite corregirlo sin perder el resto', async () => {
    editar(); await elegir(/^Otra$/);
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    expect(await screen.findByText('Escribe la dirección de entrega.')).toBeVisible();
    expect(mock.mutate).not.toHaveBeenCalled();
    fireEvent.change(screen.getByRole('textbox', { name: 'Dirección de entrega' }), { target: { value: 'Entrega de prueba personalizada' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(mock.mutate).toHaveBeenCalled());
    expect(mock.mutate.mock.calls[0][0].command.direccionEntrega).toBe('Entrega de prueba personalizada');
  });

  it('un catálogo fallido conserva el snapshot y deja capturar Otra', async () => {
    mock.catalogoError = new Error('Catálogo no disponible');
    editar('Entrega histórica'); await elegir(/^Otra$/);
    expect(screen.getByRole('textbox', { name: 'Dirección de entrega' })).toHaveValue('Entrega histórica');
    fireEvent.change(screen.getByRole('textbox', { name: 'Dirección de entrega' }), { target: { value: 'Otra entrega' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(mock.mutate).toHaveBeenCalled());
    expect(mock.mutate.mock.calls[0][0].command.direccionEntrega).toBe('Otra entrega');
  });

  it('volver a elegir Otra vacía no desactiva su validación', async () => {
    editar(); await elegir(/^Otra$/); await elegir(/^Otra$/);
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    expect(await screen.findByText('Escribe la dirección de entrega.')).toBeVisible();
    expect(mock.mutate).not.toHaveBeenCalled();
  });

  it('excluye sedes inactivas y sin domicilio y no exige rellenar un histórico vacío', async () => {
    editar();
    fireEvent.click(screen.getByRole('combobox', { name: 'Sede de entrega' }));
    expect(screen.queryByRole('option', { name: /Inactiva/ })).toBeNull();
    expect(screen.queryByRole('option', { name: /Sin domicilio/ })).toBeNull();
    fireEvent.keyDown(screen.getByPlaceholderText('Buscar sede de entrega…'), { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('option', { name: /^Otra$/ })).toBeNull());
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(mock.mutate).toHaveBeenCalled());
    expect(mock.mutate.mock.calls[0][0].command.direccionEntrega).toBeNull();
  });

  it('sin permiso no ofrece edición de la dirección', () => {
    useAuthStore.setState({ permisos: [] } as never);
    render(<InformacionLogisticaForm oc={oc('Histórica')} />);
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull();
    expect(screen.getByText('Histórica')).toBeVisible();
  });
});
