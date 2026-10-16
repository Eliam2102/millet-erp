import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { ExpedientesPage } from './ExpedientesPage';
import { ExpedienteForm } from '../components/ExpedienteForm';
import { HitosPanel } from '../components/HitosPanel';
import { DocumentosPanel } from '../components/DocumentosPanel';
import type { ExpedienteDetalle, ExpedienteFila } from '../api/types';
const state = vi.hoisted(() => ({
  filtros: vi.fn(),
  search: {} as { sucursalId?: string },
  lista: {
    data: { items: [] as ExpedienteFila[], total: 0 },
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  },
  crear: vi.fn(),
  accion: vi.fn(),
  navigate: vi.fn(),
}));
vi.mock('../components/nuevo-expediente-context', () => ({
  useNuevoExpediente: () => ({ abrir: vi.fn() }),
}));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useSearch: () => state.search,
  useNavigate: () => state.navigate,
}));
vi.mock('../api/hooks', () => ({
  CE_BASE: '/api/v1/comercio-exterior/expedientes',
  ceKeys: { detalle: (id: string) => ['ce', id] },
  useExpedientes: (filtros: unknown) => {
    state.filtros(filtros);
    return state.lista;
  },
  useCrearExpediente: () => ({ mutateAsync: state.crear, isPending: false, isError: false }),
  useAccionExpediente: () => ({ mutateAsync: state.accion, isPending: false, isError: false }),
  useOrdenesImportacion: () => ({
    data: [
      {
        id: 'oc-1',
        folio: 'OC-DEMO2026-000201',
        sucursalId: 'mid',
        moneda: 'USD',
        informacionImportacion: { incotermId: 'exw' },
      },
    ],
    isLoading: false,
  }),
}));
vi.mock('@/components/erp/selectors/CatalogoEagerCombobox', () => ({
  CatalogoEagerCombobox: ({ onChange }: { onChange: (id: string) => void }) => (
    <button type="button" onClick={() => onChange('oc-1')}>
      Seleccionar OC de importación
    </button>
  ),
}));
vi.mock('@/components/erp/selectors/IncotermSelector', () => ({
  IncotermSelector: () => <span>Incoterm seleccionado</span>,
}));
vi.mock('@/components/erp/selectors/MonedaSelector', () => ({
  MonedaSelector: () => <span>Moneda seleccionada</span>,
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: ({ onChange }: { onChange: (id: string) => void }) => (
    <button onClick={() => onChange('mty')}>Sucursal seleccionada</button>
  ),
}));
vi.mock('@/components/erp/selectors/UsuarioSelector', () => ({ UsuarioSelector: () => null }));
const data: ExpedienteDetalle = {
  expediente: {
    id: 'ce-1',
    folio: 'CE-IMP-2026-900001',
    tipo: 1,
    referencia: 'DEMO',
    sucursalId: 'mid',
    sucursalNombre: 'Mérida',
    estado: 1,
    revisionPendiente: true,
    moneda: 'USD',
    responsableNombre: 'DEMO responsable',
    version: 2,
    hitosAtrasados: 0,
    avisos: 0,
  },
  ocId: 'oc-1',
  ocFolio: 'OC-DEMO2026-000201',
  informacionImportacion: null,
  incotermId: 'exw',
  incotermCodigo: 'EXW',
  responsableUsuarioId: 'u-1',
  tc: null,
  fechaTc: null,
  fuenteTc: null,
  motivoCancelacion: null,
  revisadoPorId: null,
  participantes: [],
  requisitos: [
    {
      id: 'pedimento',
      codigo: 'IMP_PEDIMENTO',
      nombre: 'Pedimento',
      obligatorio: true,
      condicional: false,
    },
    { id: 'isf', codigo: 'EXP_ISF', nombre: 'ISF', obligatorio: false, condicional: true },
  ],
  faltantes: [
    {
      id: 'pedimento',
      codigo: 'IMP_PEDIMENTO',
      nombre: 'Pedimento',
      obligatorio: true,
      condicional: false,
    },
  ],
  estadoDocumental: 'Incompleto',
  estadoFisico: 'Pendiente',
  estadoFinanciero: 'Pendiente · se habilita con CE-03',
  hitos: [],
  avisos: [],
  documentos: [
    {
      id: 'd-1',
      tipoDocumentoId: 'pedimento',
      numeroVersion: 1,
      adjuntoId: 'a-1',
      reemplazaA: null,
      motivo: null,
      noAplica: false,
      estado: 2,
      responsableNombre: 'DEMO responsable',
      createdAt: '2026-10-15T00:00:00Z',
    },
    {
      id: 'd-2',
      tipoDocumentoId: 'pedimento',
      numeroVersion: 2,
      adjuntoId: 'a-2',
      reemplazaA: 'd-1',
      motivo: 'Rectificación DEMO',
      noAplica: false,
      estado: 1,
      responsableNombre: 'DEMO responsable',
      createdAt: '2026-10-15T01:00:00Z',
    },
  ],
};
beforeEach(() => {
  vi.clearAllMocks();
  state.search = {};
  state.lista.data = { items: [], total: 0 };
  state.lista.isError = false;
  state.lista.isLoading = false;
  useAuthStore.setState({
    permisos: [P.ComercioExteriorExpedientesLeer, P.ComercioExteriorExpedientesCrear],
    user: { id: 'u-1', nombre: 'DEMO responsable', email: 'ce@example.invalid' },
    currentSucursalId: 'mid',
  });
});
describe('CE-01a', () => {
  it('muestra listado vacío con siguiente acción', () => {
    render(<ExpedientesPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('Sin expedientes')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nuevo expediente' })).toBeInTheDocument();
  });
  it('muestra atraso con punto de 7 px y frase', () => {
    state.lista.data = { items: [{ ...data.expediente, hitosAtrasados: 1 }], total: 1 };
    render(<ExpedientesPage />, { wrapper: createQueryWrapper() });
    const indicador = screen.getByText('1 hito vencido');
    expect(indicador.querySelector('[aria-hidden="true"]')).toHaveClass(
      'size-[7px]',
      'bg-danger-solid',
    );
  });
  it('muestra error recuperable en listado', () => {
    state.lista.isError = true;
    render(<ExpedientesPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('No se pudieron cargar los expedientes')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(state.lista.refetch).toHaveBeenCalled();
  });
  it('oculta alta sin permiso', () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesLeer] });
    render(<ExpedientesPage />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: 'Nuevo expediente' })).not.toBeInTheDocument();
  });
  it('sin lectura global consulta la sucursal activa', () => {
    render(<ExpedientesPage />, { wrapper: createQueryWrapper() });
    expect(state.filtros).toHaveBeenLastCalledWith(expect.objectContaining({ sucursalId: 'mid' }));
    expect(screen.queryByRole('button', { name: 'Todas' })).not.toBeInTheDocument();
  });
  it('con lectura global ofrece Todas y permite elegir otra sucursal', () => {
    useAuthStore.setState({
      permisos: [
        P.ComercioExteriorExpedientesLeer,
        P.ComercioExteriorDocumentosLeerTodasSucursales,
      ],
    });
    state.search = { sucursalId: 'todas' };
    render(<ExpedientesPage />, { wrapper: createQueryWrapper() });
    expect(state.filtros).toHaveBeenLastCalledWith(
      expect.objectContaining({ sucursalId: undefined }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Todas' }));
    expect(state.navigate).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: { sucursalId: 'todas', offset: 0 } }),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Sucursal seleccionada' }));
    expect(state.navigate).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: { sucursalId: 'mty', offset: 0 } }),
    );
  });
  it('alta desde OC propone moneda e Incoterm y envía el origen', async () => {
    state.crear.mockResolvedValue({ data });
    render(<ExpedienteForm onClose={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('button', { name: 'Crear expediente' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Seleccionar OC de importación' }));
    fireEvent.change(screen.getByLabelText('Referencia de la operación'), {
      target: { value: 'DEMO importación' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Crear expediente' }));
    await waitFor(() =>
      expect(state.crear).toHaveBeenCalledWith(
        expect.objectContaining({
          tipo: 1,
          ocId: 'oc-1',
          incotermId: 'exw',
          moneda: 'USD',
          sucursalId: 'mid',
          responsableUsuarioId: 'u-1',
        }),
      ),
    );
  });
  it('muestra faltantes, versiones y motivo de sustitución', () => {
    render(<DocumentosPanel data={data} />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('note')).toHaveTextContent('Faltan: Pedimento');
    expect(screen.getByText('Versión 1')).toBeInTheDocument();
    expect(screen.getByText('Versión 2')).toBeInTheDocument();
    expect(screen.getByText('Reemplazada')).toBeInTheDocument();
    expect(screen.getByText('Motivo: Rectificación DEMO')).toBeInTheDocument();
  });
  it('No aplica requiere motivo y sólo aparece en un condicional', () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesAdjuntosSubir] });
    render(<DocumentosPanel data={data} />, { wrapper: createQueryWrapper() });
    const boton = screen.getByRole('button', { name: 'No aplica' });
    expect(boton).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Motivo de sustitución/), {
      target: { value: 'DEMO ruta sin ISF' },
    });
    expect(boton).toBeEnabled();
    fireEvent.click(boton);
    expect(state.accion).toHaveBeenCalledWith({
      accion: 'documentos',
      body: {
        tipoDocumentoId: 'isf',
        adjuntoId: null,
        noAplica: true,
        motivo: 'DEMO ruta sin ISF',
      },
    });
  });
  it('la revisión documental tiene permiso propio', () => {
    render(<DocumentosPanel data={data} />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: 'Revisar documentos' })).not.toBeInTheDocument();
  });
});

describe('CE-01b · Hitos', () => {
  const hito = {
    id: 'h-2',
    clave: 'planeado',
    nombre: 'Planeado',
    orden: 2,
    fechaCompromiso: '2026-10-14',
    fechaReal: null,
    responsableUsuarioId: 'u-1',
    responsableNombre: 'DEMO responsable',
    evidenciaAdjuntoId: null,
    omitido: false,
    motivo: null,
    motivoCambioCompromiso: null,
    atrasado: true,
    diasAtraso: 2,
  };
  const conHitos = () => ({
    ...data,
    hitos: [hito],
    avisos: [
      {
        id: 'av-1',
        hitoId: 'h-2',
        hitoNombre: 'Planeado',
        fechaCompromiso: '2026-10-14',
        fechaAviso: '2026-10-16',
      },
    ],
  });
  it('muestra compromiso, real, atraso del servidor y aviso histórico', () => {
    render(<HitosPanel data={conHitos()} />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('2 días de atraso')).toBeInTheDocument();
    expect(screen.getByText('Sin fecha real')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Avisos de hitos vencidos' })).toHaveTextContent(
      'Compromiso:',
    );
    expect(screen.getByRole('note')).toHaveTextContent('Pendiente · se habilita con CE-03');
    expect(screen.queryByRole('button', { name: 'Actualizar Planeado' })).not.toBeInTheDocument();
  });
  it('reprograma con motivo y conserva el compromiso anterior hasta guardar', async () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesHitosRegistrar] });
    state.accion.mockResolvedValue({ data });
    render(<HitosPanel data={conHitos()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar Planeado' }));
    fireEvent.change(screen.getByLabelText('Fecha compromiso'), {
      target: { value: '2026-10-20' },
    });
    const motivo = screen.getByLabelText(/Motivo \(obligatorio/);
    expect(motivo).toBeRequired();
    fireEvent.change(motivo, { target: { value: 'DEMO cambio de ruta' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar hito' }));
    await waitFor(() =>
      expect(state.accion).toHaveBeenCalledWith(
        expect.objectContaining({
          accion: 'hitos/planeado/compromiso',
          body: expect.objectContaining({
            fechaCompromiso: '2026-10-20',
            motivo: 'DEMO cambio de ruta',
          }),
        }),
      ),
    );
  });
  it('no ofrece omitir documentación completa', () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesHitosRegistrar] });
    render(
      <HitosPanel
        data={{
          ...data,
          hitos: [{ ...hito, clave: 'documentacion-completa', nombre: 'Documentación completa' }],
        }}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar Documentación completa' }));
    expect(screen.queryByRole('option', { name: 'Omitir con motivo' })).not.toBeInTheDocument();
  });
  it('registra fecha real con el responsable del hito', async () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesHitosRegistrar] });
    state.accion.mockResolvedValue({ data });
    render(<HitosPanel data={conHitos()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar Planeado' }));
    fireEvent.change(screen.getByLabelText('Acción del hito'), { target: { value: 'real' } });
    fireEvent.change(screen.getByLabelText('Fecha real'), { target: { value: '2026-10-16' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar hito' }));
    await waitFor(() =>
      expect(state.accion).toHaveBeenCalledWith(
        expect.objectContaining({
          accion: 'hitos/planeado/real',
          body: expect.objectContaining({
            fechaReal: '2026-10-16',
            fechaCompromiso: null,
            responsableUsuarioId: 'u-1',
          }),
        }),
      ),
    );
  });
  it('conserva el aviso al completar y deja de mostrar atraso', () => {
    const d = conHitos();
    render(
      <HitosPanel
        data={{
          ...d,
          hitos: [{ ...hito, fechaReal: '2026-10-16', atrasado: false, diasAtraso: 0 }],
        }}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('Registrado')).toBeInTheDocument();
    expect(screen.queryByText('2 días de atraso')).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Avisos de hitos vencidos' })).toHaveTextContent(
      'Compromiso:',
    );
  });
  it('bloquea Cerrado y explica CE-03', () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesHitosRegistrar] });
    render(
      <HitosPanel
        data={{ ...data, hitos: [{ ...hito, clave: 'cerrado', nombre: 'Cerrado', orden: 15 }] }}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByRole('button', { name: 'Registrar Cerrado' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Registrar Cerrado' })).toHaveAttribute(
      'title',
      'Pendiente · se habilita con CE-03',
    );
  });
  it('el filtro Con atraso se envía al servidor', () => {
    render(<ExpedientesPage />, { wrapper: createQueryWrapper() });
    fireEvent.change(screen.getByLabelText('Filtrar atraso'), { target: { value: 'true' } });
    expect(state.navigate).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: { atraso: true, offset: 0 } }),
    );
  });
});
