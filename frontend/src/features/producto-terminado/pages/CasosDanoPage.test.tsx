import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { CasosDanoPage } from './CasosDanoPage';

const h = vi.hoisted(() => ({ casos: vi.fn(), accion: vi.fn(), evidencia: vi.fn() }));

vi.mock('./TramoDetenidoBanner', () => ({ TramoDetenidoBanner: () => null }));
vi.mock('../lib/usuarios', () => ({ useNombreUsuario: () => (id?: string | null) => id ?? '—' }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/components/erp', async (importOriginal) => ({ ...(await importOriginal<typeof import('@/components/erp')>()), Breadcrumbs: () => null }));
vi.mock('@/features/catalogos/api', () => ({ useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Conkal' }] } }) }));
vi.mock('../api/hooks', () => ({
  useCasos: h.casos,
  useAccionCaso: () => ({ mutateAsync: h.accion, isPending: false }),
  useEvidenciaCaso: h.evidencia,
}));

const caso = (p: object = {}) => ({
  id: 'c1', folio: 'CD-0001', numeroPedido: 'PED-9', sucursalId: 'suc-1', etapa: 'RechazoEnSitio', motivo: 'Vidrio roto al descargar', tieneEvidencia: true, estado: 'Abierto',
  piezas: [{ etiqueta: 'PED-9-1', posicion: 1, estado: 'EnRevision' }], registradoPorId: 'u1', registradoAt: '2026-11-10T10:00:00Z',
  dictamen: null, observacionDictamen: null, pedidoRefabricacionAw: null, dictaminadoPorId: null, dictaminadoAt: null, facturaFolio: null, correctivo: null, documento: null, cerradoAt: null, ...p,
});
const lista = (items: object[]) => h.casos.mockReturnValue({ data: { items, total: items.length }, isLoading: false, isError: false, refetch: vi.fn() });

beforeEach(() => {
  vi.clearAllMocks();
  h.accion.mockResolvedValue(caso());
  h.evidencia.mockReturnValue({ data: { firma: null, foto: 'data:image/jpeg;base64,AAAA' }, isLoading: false, isError: false });
  useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar, P.ProductoTerminadoCalidadDictaminar] });
});

describe('CasosDanoPage', () => {
  it('lista vacía y sin caso elegido', () => {
    lista([]);
    render(<CasosDanoPage />);
    expect(screen.getByText('No hay casos para esos filtros')).toBeInTheDocument();
    expect(screen.getByText('Elige un caso de la lista para ver su detalle')).toBeInTheDocument();
  });

  it('muestra el detalle con la evidencia y dictamina refabricación con su pedido A+W', async () => {
    lista([caso()]);
    render(<CasosDanoPage />);
    fireEvent.click(screen.getByRole('button', { name: /CD-0001/ }));
    expect(screen.getByAltText('Evidencia del caso CD-0001')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Dictamen'), { target: { value: 'Refabricacion' } });
    expect(screen.getByText(/se pide como pedido nuevo en A\+W/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/Pedido de refabricación en A\+W/), { target: { value: '10425999' } });
    fireEvent.change(screen.getByLabelText('Observación'), { target: { value: 'Se rompió en la descarga' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar dictamen' }));
    await waitFor(() => expect(h.accion).toHaveBeenCalledWith({ tipo: 'dictaminar', id: 'c1', body: { dictamen: 'Refabricacion', observacion: 'Se rompió en la descarga', pedidoRefabricacionAw: '10425999' } }));
  });

  it('el correctivo fiscal solo aparece dictaminado y con factura; la cancelación 01 exige el UUID sustituto', async () => {
    lista([caso({ estado: 'Dictaminado', dictamen: 'Desecho', observacionDictamen: 'Sin reparación', facturaFolio: 'FCNK-1234' })]);
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar, P.FacturacionNotasCreditoBonificacion, P.FacturacionCancelacionesSolicitar] });
    render(<CasosDanoPage />);
    fireEvent.click(screen.getByRole('button', { name: /CD-0001/ }));
    expect(screen.getByText('Desecho')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Emitir nota de crédito' }));
    await waitFor(() => expect(h.accion).toHaveBeenCalledWith({ tipo: 'nota-credito', id: 'c1' }));
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar factura' }));
    fireEvent.change(screen.getByLabelText('Motivo SAT'), { target: { value: '01' } });
    expect(screen.getByRole('button', { name: 'Solicitar cancelación' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/UUID de la factura/), { target: { value: '123e4567-e89b-12d3-a456-426614174000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar cancelación' }));
    await waitFor(() => expect(h.accion).toHaveBeenCalledWith({ tipo: 'cancelar-factura', id: 'c1', body: { motivoSat: '01', uuidSustituto: '123e4567-e89b-12d3-a456-426614174000' } }));
  });

  it('abierto no muestra correctivo aunque haya factura, y cerrado no ofrece acciones', () => {
    lista([caso({ facturaFolio: 'FCNK-1' }), caso({ id: 'c2', folio: 'CD-0002', estado: 'Cerrado', dictamen: 'Reproceso', facturaFolio: 'FCNK-2', cerradoAt: '2026-11-11T10:00:00Z' })]);
    render(<CasosDanoPage />);
    fireEvent.click(screen.getByRole('button', { name: /CD-0001/ }));
    expect(screen.queryByLabelText('Correctivo fiscal')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /CD-0002/ }));
    expect(screen.queryByRole('button', { name: 'Cerrar caso' })).not.toBeInTheDocument();
    expect(screen.getByText('Reproceso')).toBeInTheDocument();
  });

  it('registrar caso valida etiquetas y motivo y envía las etiquetas sin repetir', async () => {
    lista([]);
    h.accion.mockResolvedValue(caso());
    render(<CasosDanoPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Registrar caso' }));
    const enviar = screen.getAllByRole('button', { name: 'Registrar caso' }).at(-1)!;
    expect(enviar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Etiquetas de las piezas'), { target: { value: 'PED-9-1\nPED-9-2\nPED-9-1' } });
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'Rayón profundo' } });
    fireEvent.click(enviar);
    await waitFor(() => expect(h.accion).toHaveBeenCalledWith({ tipo: 'registrar', body: { etiquetas: ['PED-9-1', 'PED-9-2'], motivo: 'Rayón profundo' } }));
  });

  it('error al cargar con reintento', () => {
    const refetch = vi.fn();
    h.casos.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    render(<CasosDanoPage />);
    fireEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(refetch).toHaveBeenCalled();
  });
});
