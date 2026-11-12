import * as imprimirPdf from '@/components/erp/reportes/imprimir-pdf';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { TraspasosPage } from './TraspasosPage';

const state = vi.hoisted(() => ({
  lista: vi.fn(), detalle: vi.fn(), recibir: vi.fn(), crear: vi.fn(), autorizar: vi.fn(), clasificar: vi.fn(), aclarar: vi.fn(),
}));

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  Breadcrumbs: () => null,
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: ({ placeholder, onChange }: { placeholder?: string; onChange: (id: string) => void }) => (
    <select aria-label={placeholder} onChange={(e) => onChange(e.target.value)}>
      <option value="">-</option>
      <option value="suc-1">Conkal</option>
      <option value="suc-2">Cancún</option>
    </select>
  ),
}));
vi.mock('@/features/facturacion/components/VehiculoPicker', () => ({
  VehiculoPicker: ({ onChange }: { onChange: (id: string) => void }) => <button onClick={() => onChange('veh-1')}>Elegir vehículo</button>,
}));
vi.mock('@/features/facturacion/components/OperadorPicker', () => ({
  OperadorPicker: ({ onChange }: { onChange: (id: string) => void }) => <button onClick={() => onChange('ope-1')}>Elegir operador</button>,
}));
vi.mock('@/features/almacen/components/EvidenciaUpload', () => ({
  EvidenciaUpload: ({ onChange }: { onChange: (v: unknown) => void }) => (
    <button onClick={() => onChange({ blobRef: 'blob://evidencias/rota.jpg', nombreArchivo: 'rota.jpg', tamanoBytes: 10, contentType: 'image/jpeg' })}>Subir evidencia</button>
  ),
}));
vi.mock('@/features/catalogos/api', () => ({
  useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Conkal' }, { id: 'suc-2', nombre: 'Cancún' }] } }),
}));
vi.mock('../api/hooks', () => ({
  EVIDENCIA_TRASPASO_URL: '/evidencia',
  useTraspasos: state.lista,
  useTraspaso: state.detalle,
  useRecibirTraspaso: () => ({ mutateAsync: state.recibir, isPending: false }),
  useCrearTraspaso: () => ({ mutateAsync: state.crear, isPending: false }),
  useAutorizarTraspaso: () => ({ mutateAsync: state.autorizar, isPending: false }),
  useClasificarDiferencias: () => ({ mutateAsync: state.clasificar, isPending: false }),
  useAclararDiferencia: () => ({ mutateAsync: state.aclarar, isPending: false }),
}));

const traspaso = (estado: string, pendientes: number, extra: object = {}) => ({
  id: 't1', folio: 'TR-20261104-ABCDE', sucursalOrigenId: 'suc-1', sucursalDestinoId: 'suc-2', estado, salidaAt: '2026-11-04T12:00:00Z', recibidoAt: null, total: 2, pendientes, piezas: [],
  preparadoPorId: 'u-prepara', autorizadoPorId: null, autorizadoAt: null, foranea: false, destino: null, cartaPorteFolio: null, ...extra,
});
const pieza = (piezaId: string, etiqueta: string, extra: object = {}) => ({
  piezaId, etiqueta, numeroPedido: 'P-1', recibida: false, clasificacion: null, motivo: null, evidencia: null, resolucion: null, ...extra,
});
const detalle = {
  ...traspaso('ConDiferencia', 1),
  piezas: [pieza('p1', 'E1', { recibida: true }), pieza('p2', 'E2')],
};
const listaCon = (items: unknown[]) => ({ data: { items, total: items.length }, isLoading: false, isError: false, refetch: vi.fn() });

beforeEach(() => {
  vi.clearAllMocks();
  state.recibir.mockResolvedValue({});
  state.crear.mockResolvedValue({ data: { id: 't1' } });
  state.autorizar.mockResolvedValue({});
  state.clasificar.mockResolvedValue({});
  state.aclarar.mockResolvedValue({});
  state.detalle.mockReturnValue({ data: undefined });
  useAuthStore.setState({ user: { id: 'u-autoriza', email: 'a@x.mx', nombre: 'Autoriza' }, permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar] });
});

describe('TraspasosPage', () => {
  it('muestra el traspaso en tránsito con su folio y pendientes y recibe todo', () => {
    state.lista.mockReturnValue(listaCon([traspaso('EnTransito', 2)]));
    render(<TraspasosPage />);
    expect(screen.getByText('En tránsito')).toBeInTheDocument();
    expect(screen.getByText('TR-20261104-ABCDE')).toBeInTheDocument();
    expect(screen.getByText('2 de 2')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Recibir todo' }));
    expect(state.recibir).toHaveBeenCalledWith({ id: 't1' });
  });

  it('recepción parcial: solo manda las piezas marcadas', () => {
    state.lista.mockReturnValue(listaCon([traspaso('ConDiferencia', 1)]));
    state.detalle.mockReturnValue({ data: detalle });
    render(<TraspasosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver piezas' }));
    expect(screen.getByText('Con diferencia')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Recibir E2' }));
    fireEvent.click(screen.getByRole('button', { name: 'Recibir seleccionadas' }));
    expect(state.recibir).toHaveBeenCalledWith({ id: 't1', piezaIds: ['p2'] });
  });

  it('prepara el traspaso con las etiquetas de cada renglón', () => {
    state.lista.mockReturnValue(listaCon([]));
    render(<TraspasosPage />);
    fireEvent.change(screen.getByLabelText('Sucursal origen'), { target: { value: 'suc-1' } });
    fireEvent.change(screen.getByLabelText('Sucursal destino'), { target: { value: 'suc-2' } });
    fireEvent.change(screen.getByLabelText('Etiquetas de las piezas'), { target: { value: 'E1\n E2 \n' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preparar traspaso' }));
    expect(state.crear).toHaveBeenCalledWith({ sucursalOrigenId: 'suc-1', sucursalDestinoId: 'suc-2', etiquetas: ['E1', 'E2'] });
  });

  it('foráneo: pide vehículo, operador y datos de la Carta Porte antes de preparar', () => {
    state.lista.mockReturnValue(listaCon([]));
    render(<TraspasosPage />);
    fireEvent.change(screen.getByLabelText('Sucursal origen'), { target: { value: 'suc-1' } });
    fireEvent.change(screen.getByLabelText('Sucursal destino'), { target: { value: 'suc-2' } });
    fireEvent.change(screen.getByLabelText('Etiquetas de las piezas'), { target: { value: 'E1' } });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Traspaso foráneo o por carretera federal' }));
    expect(screen.getByRole('button', { name: 'Preparar traspaso' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Destino del traslado'), { target: { value: 'Cancún' } });
    fireEvent.click(screen.getByRole('button', { name: 'Elegir vehículo' }));
    fireEvent.click(screen.getByRole('button', { name: 'Elegir operador' }));
    fireEvent.change(screen.getByPlaceholderText('320'), { target: { value: '1300' } });
    fireEvent.change(screen.getByPlaceholderText('1850'), { target: { value: '800' } });
    fireEvent.change(screen.getByPlaceholderText('97302'), { target: { value: '97302' } });
    fireEvent.change(screen.getByPlaceholderText('YUC'), { target: { value: 'YUC' } });
    fireEvent.change(screen.getByPlaceholderText('77500'), { target: { value: '77500' } });
    fireEvent.change(screen.getByPlaceholderText('ROO'), { target: { value: 'ROO' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preparar traspaso' }));

    expect(state.crear).toHaveBeenCalledWith({
      sucursalOrigenId: 'suc-1', sucursalDestinoId: 'suc-2', etiquetas: ['E1'], foranea: true,
      transporte: { destino: 'Cancún', vehiculoId: 'veh-1', operadorId: 'ope-1', distanciaKm: 1300, pesoKg: 800, origenCodigoPostal: '97302', origenEstado: 'YUC', destinoCodigoPostal: '77500', destinoEstado: 'ROO' },
    });
  });

  it('autoriza un traspaso preparado por otra persona; quien lo preparó no ve Autorizar', () => {
    state.lista.mockReturnValue(listaCon([traspaso('Preparado', 2)]));
    const { unmount } = render(<TraspasosPage />);
    expect(screen.getByText('Por autorizar')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Recibir todo' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    expect(state.autorizar).toHaveBeenCalledWith('t1');
    unmount();

    useAuthStore.setState({ user: { id: 'u-prepara', email: 'p@x.mx', nombre: 'Prepara' } });
    render(<TraspasosPage />);
    expect(screen.queryByRole('button', { name: 'Autorizar' })).toBeNull();
    expect(screen.getByText('Lo autoriza otra persona')).toBeInTheDocument();
  });

  it('imprime la hoja de traslado con su folio', () => {
    const imprimir = vi.spyOn(imprimirPdf, 'abrirPdfParaImprimir').mockResolvedValue(undefined);
    state.lista.mockReturnValue(listaCon([traspaso('EnTransito', 2)]));
    state.detalle.mockReturnValue({ data: { ...traspaso('EnTransito', 2), piezas: [pieza('p1', 'E1'), pieza('p2', 'E2')] } });
    render(<TraspasosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver piezas' }));
    expect(screen.getByRole('heading', { name: 'Hoja de traslado TR-20261104-ABCDE' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Imprimir hoja de traslado/ }));
    expect(imprimir).toHaveBeenCalledOnce();
  });

  it('«Por recibir» pide los traspasos en tránsito o con diferencia de la sucursal que recibe', () => {
    state.lista.mockReturnValue(listaCon([traspaso('EnTransito', 2)]));
    render(<TraspasosPage />);
    expect(state.lista).toHaveBeenLastCalledWith({ pageSize: 50 });
    fireEvent.click(screen.getByRole('button', { name: 'Por recibir' }));
    fireEvent.change(screen.getByLabelText('Sucursal que recibe'), { target: { value: 'suc-2' } });
    expect(state.lista).toHaveBeenLastCalledWith({ estado: 'EnTransito,ConDiferencia', destinoId: 'suc-2', pageSize: 50 });
  });

  it('clasifica la diferencia con motivo y exige evidencia si llegó dañada', () => {
    state.lista.mockReturnValue(listaCon([traspaso('ConDiferencia', 1)]));
    state.detalle.mockReturnValue({ data: detalle });
    render(<TraspasosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver piezas' }));
    const clasificar = screen.getByRole('button', { name: 'Clasificar diferencias' });
    expect(clasificar).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Clasificación de E2'), { target: { value: 'DanadaEnTransito' } });
    fireEvent.change(screen.getByLabelText('Motivo de E2'), { target: { value: 'Llegó rota' } });
    expect(clasificar).toBeDisabled(); // dañada: falta la evidencia
    fireEvent.click(screen.getByRole('button', { name: 'Subir evidencia' }));
    expect(clasificar).toBeEnabled();
    fireEvent.click(clasificar);

    expect(state.clasificar).toHaveBeenCalledWith({ id: 't1', piezas: [{ piezaId: 'p2', clasificacion: 'DanadaEnTransito', motivo: 'Llegó rota', evidencia: 'blob://evidencias/rota.jpg' }] });
  });

  it('un faltante se clasifica sin evidencia', () => {
    state.lista.mockReturnValue(listaCon([traspaso('ConDiferencia', 1)]));
    state.detalle.mockReturnValue({ data: detalle });
    render(<TraspasosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver piezas' }));
    fireEvent.change(screen.getByLabelText('Clasificación de E2'), { target: { value: 'Faltante' } });
    fireEvent.change(screen.getByLabelText('Motivo de E2'), { target: { value: 'No venía en el camión' } });
    fireEvent.click(screen.getByRole('button', { name: 'Clasificar diferencias' }));
    expect(state.clasificar).toHaveBeenCalledWith({ id: 't1', piezas: [{ piezaId: 'p2', clasificacion: 'Faltante', motivo: 'No venía en el camión', evidencia: null }] });
  });

  it('aclara la pieza clasificada: regresa al origen o se da por perdida', () => {
    state.lista.mockReturnValue(listaCon([traspaso('ConDiferencia', 1)]));
    state.detalle.mockReturnValue({ data: { ...detalle, piezas: [pieza('p1', 'E1', { recibida: true }), pieza('p2', 'E2', { clasificacion: 'Faltante', motivo: 'No venía' })] } });
    render(<TraspasosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver piezas' }));
    expect(screen.queryByLabelText('Clasificación de E2')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Regresar E2 al origen' }));
    expect(state.aclarar).toHaveBeenCalledWith({ id: 't1', piezaId: 'p2', resolucion: 'RegresadaAlOrigen' });
    fireEvent.click(screen.getByRole('button', { name: 'Dar E2 por perdida' }));
    expect(state.aclarar).toHaveBeenCalledWith({ id: 't1', piezaId: 'p2', resolucion: 'DadaPorPerdida' });
  });

  it('solo con piezas.ver no ofrece operar', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer] });
    state.lista.mockReturnValue(listaCon([traspaso('EnTransito', 2), traspaso('Preparado', 2, { id: 't2' })]));
    render(<TraspasosPage />);
    expect(screen.queryByRole('button', { name: 'Recibir todo' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Preparar traspaso' })).toBeNull();
  });
});
