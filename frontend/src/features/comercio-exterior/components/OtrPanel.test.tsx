import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { OtrPanel, OtrDetalle } from './OtrPanel';
import { OtrBandejaPage } from '../pages/OtrBandejaPage';
import type { OtrEnvio } from '../api/otr';
const state = vi.hoisted(() => ({ data: [] as OtrEnvio[], mutar: vi.fn() }));
vi.mock('../api/otr', () => ({
  useOtrEnvios: () => ({ data: state.data, isError: false }),
  useOtrAccion: () => ({ mutateAsync: state.mutar, isPending: false, isError: false }),
  useOtrCandidatos: () => ({ data: [{ id: 'exp', folio: 'CE-EXP-DEMO' }], isError: false }),
}));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));
vi.mock('@/components/erp', () => ({
  Breadcrumbs: () => null,
  ErrorState: () => <p>Error</p>,
  DateTimeDisplay: ({ value }: { value: string }) => <span>{value}</span>,
}));
vi.mock('@/components/erp/selectors/UsuarioSelector', () => ({ UsuarioSelector: () => null }));
vi.mock('@/components/erp/selectors/MonedaSelector', () => ({ MonedaSelector: () => null }));
const envio: OtrEnvio = {
  id: 'otr1',
  idOrigen: 'DEMO-OTR-1',
  version: 3,
  versionAplicada: 2,
  hash: 'abc',
  motivoVinculacion: null,
  sucursalId: 'mid',
  clienteId: 'cliente',
  expedienteId: 'exp',
  contenedorId: 'cont',
  estado: 2,
  contenido: {
    cliente: 'DEMO cliente',
    direccionEntrega: 'DEMO dirección',
    producto: 'DEMO vidrio',
    contenedor: {
      numero: 'DEMO-C',
      ruta: 'DEMO-R',
      pesoKg: 125,
      bultos: 2,
      fechaCargaPrevista: '2026-10-20',
    },
  },
  lineas: [
    {
      id: 'l1',
      versionOrigen: 2,
      numeroPedido: 'DEMO-PED',
      posicion: 1,
      cantidad: 10,
      unidad: 'H87',
      estado: 1,
      motivo: 'CE_OTR_PEDIDO_INEXISTENTE',
      partidaId: null,
    },
  ],
  conflictos: [
    {
      id: 'conf1',
      versionOrigen: 2,
      campo: 'PesoKg',
      valorOtr: '125',
      valorErp: '150',
      actorErp: 'DEMO operador',
      fechaErp: '2026-10-16T12:00:00Z',
      recibidoAt: '2026-10-16T12:01:00Z',
      estado: 'Pendiente',
      superadoPorVersion: null,
      resueltoPorId: null,
      resueltoAt: null,
      usarOtr: null,
      motivo: null,
    },
  ],
  salidas: [
    {
      id: 'sal1',
      tipo: 'hito',
      versionErp: 7,
      estado: 4,
      intentos: 5,
      intento: 1,
      reemplazadaPorId: null,
      sinAcuse: false,
      ultimoError: 'CE_OTR_SIN_ACUSE',
      acuseAt: null,
      acuseResultado: null,
      acuseMotivo: null,
    },
  ],
};
beforeEach(() => {
  state.mutar.mockReset().mockResolvedValue({});
  state.data = [envio];
  useAuthStore.setState({
    permisos: [P.ComercioExteriorExpedientesLeer, P.ComercioExteriorOtrGestionar],
  });
});
it('la bandeja muestra envíos por vincular y abre el selector de expediente', () => {
  state.data = [{ ...envio, estado: 1, expedienteId: null, salidas: [] }];
  render(<OtrBandejaPage />);
  expect(screen.getByRole('heading', { name: 'Envíos OTR por vincular' })).toBeInTheDocument();
  expect(screen.getByText('Por vincular')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Vincular envío' }));
  fireEvent.change(screen.getByLabelText('Expediente abierto'), { target: { value: 'exp' } });
  fireEvent.click(screen.getByRole('button', { name: 'Vincular envío' }));
  expect(state.mutar).toHaveBeenCalledWith({
    envio: state.data[0],
    ruta: 'vincular',
    body: { expedienteId: 'exp' },
  });
});
it('la sección muestra versión, línea pendiente, conflicto y falta de acuse', () => {
  render(<OtrPanel expedienteId="exp" />);
  expect(screen.getByText('Vinculado · versión 2')).toBeInTheDocument();
  expect(screen.getByText('CE_OTR_PEDIDO_INEXISTENTE')).toBeInTheDocument();
  expect(screen.getByText('CE_OTR_SIN_ACUSE')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Reintentar salida' }));
  expect(state.mutar).toHaveBeenCalledWith({ envio, ruta: 'salidas/sal1/reintentar' });
});
it.each([
  ['Usar OTR', true],
  ['Conservar ERP', false],
] as const)('resuelve %s sólo con motivo suficiente', (nombre, usarOtr) => {
  render(<OtrDetalle envio={envio} />);
  const button = screen.getByRole('button', { name: nombre });
  expect(button).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Motivo de la decisión (5 a 500 caracteres)'), {
    target: { value: 'DEMO decisión revisada' },
  });
  fireEvent.click(button);
  expect(state.mutar).toHaveBeenCalledWith({
    envio,
    ruta: 'conflictos/conf1/resolver',
    body: { usarOtr, motivo: 'DEMO decisión revisada' },
  });
});
it('lectura oculta reintentar y gestión lo conserva con expediente cerrado', () => {
  useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesLeer] });
  const view = render(<OtrDetalle envio={envio} />);
  expect(screen.queryByRole('button', { name: 'Usar OTR' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Reintentar salida' })).not.toBeInTheDocument();
  useAuthStore.setState({ permisos: [P.ComercioExteriorOtrGestionar] });
  view.rerender(<OtrDetalle envio={envio} abierto={false} />);
  expect(screen.getByRole('button', { name: 'Reintentar salida' })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Usar OTR' })).not.toBeInTheDocument();
});
it('la bandeja vacía explica el estado real', () => {
  state.data = [];
  render(<OtrBandejaPage />);
  expect(screen.getByText('No hay envíos OTR por vincular.')).toBeInTheDocument();
});

it('identifica el pedido y posición al resolver un conflicto de asignación manual', () => {
  const piezas = { ...envio, conflictos: [{ ...envio.conflictos[0], campo: 'Partida:l1' }] };
  render(<OtrDetalle envio={piezas} />);
  expect(screen.getByText('Pedido DEMO-PED · posición 1 · versión OTR 2')).toBeInTheDocument();
});

it.each([1, 3] as const)('permite reintentar una salida de un envío con estado %s', (estado) => {
  render(<OtrDetalle envio={{ ...envio, estado }} abierto={false} />);
  fireEvent.click(screen.getByRole('button', { name: 'Reintentar salida' }));
  expect(state.mutar).toHaveBeenCalled();
});
it('la bandeja incluye fallidas y sin acuse de expedientes vinculados', () => {
  state.data = [
    envio,
    {
      ...envio,
      id: 'otr2',
      idOrigen: 'DEMO-OTR-2',
      salidas: [{ ...envio.salidas[0], estado: 2, sinAcuse: true }],
    },
  ];
  render(<OtrBandejaPage />);
  expect(screen.getByRole('heading', { name: 'Salidas fallidas o sin acuse' })).toBeInTheDocument();
  expect(screen.getByText('Sin acuse')).toBeInTheDocument();
  expect(screen.getByText('Fallida')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Vincular envío' })).not.toBeInTheDocument();
});
