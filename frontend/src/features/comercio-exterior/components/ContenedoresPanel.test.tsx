import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { ContenedoresPanel } from './ContenedoresPanel';
import { EmbarqueCeResumen } from './EmbarqueCeResumen';
import type { ContenedoresDetalle, ExpedienteDetalle } from '../api/types';
const state = vi.hoisted(() => ({ mutar: vi.fn(), data: null as ContenedoresDetalle | null }));
vi.mock('../api/hooks', () => ({
  useContenedores: () => ({ data: { data: state.data }, isError: false }),
  useMutarContenedor: () => ({ mutateAsync: state.mutar, isPending: false, isError: false }),
  usePedidosEmbarcables: () => ({
    data: [
      {
        id: 'p1',
        numeroPedido: 'DEMO-P1',
        lineas: [{ posicion: 1, descripcion: 'DEMO vidrio', cantidad: 100, unidad: 'H87' }],
      },
    ],
  }),
}));
vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  ErrorState: () => <p>Error</p>,
  DateTimeDisplay: ({ value }: { value: string }) => <span>{value}</span>,
}));
const data: ExpedienteDetalle = {
  expediente: {
    id: 'exp',
    folio: 'CE-EXP-DEMO',
    tipo: 2,
    referencia: 'DEMO exportación',
    sucursalId: 'mid',
    sucursalNombre: 'Mérida',
    estado: 1,
    revisionPendiente: false,
    moneda: 'USD',
    responsableNombre: 'DEMO responsable',
    version: 1,
    hitosAtrasados: 0,
    avisos: 0,
  },
  ocId: null,
  ocFolio: null,
  informacionImportacion: null,
  incotermId: 'ddp',
  incotermCodigo: 'DDP',
  responsableUsuarioId: 'actor',
  tc: null,
  fechaTc: null,
  fuenteTc: null,
  motivoCancelacion: null,
  revisadoPorId: null,
  requisitos: [],
  documentos: [],
  faltantes: [],
  estadoDocumental: 'Por revisar',
  estadoFisico: 'Pendiente',
  estadoFinanciero: 'Pendiente',
  hitos: [],
  avisos: [],
  participantes: [
    {
      id: 'cli',
      clienteId: 'cli',
      nombre: 'DEMO cliente',
      tipo: 7,
      descripcion: null,
      contacto: null,
      papel: 1,
      proveedorId: null,
    },
  ],
};
beforeEach(() => {
  state.mutar.mockReset().mockResolvedValue({});
  useAuthStore.setState({ permisos: [P.ComercioExteriorContenedoresGestionar] });
  state.data = {
    version: 1,
    pedimento: { numero: 'DEMO-PED', clave: 'A1', fecha: '2026-10-12' },
    contenedores: [
      {
        id: 'c1',
        numero: 'DEMO-C1',
        sello: null,
        booking: null,
        blGuia: 'DEMO-BL',
        ruta: null,
        fechaCargaPrevista: null,
        fechaEmbarqueReal: null,
        pesoKg: null,
        bultos: null,
        estado: 1,
        motivoCancelacion: null,
      },
    ],
    partidas: [
      {
        id: 'partida1',
        contenedorId: 'c1',
        pedidoFacturableId: 'p1',
        posicion: 1,
        clienteId: 'cli',
        cantidad: 60,
        unidad: 'H87',
        versionOrigenAsignada: 1,
        estado: 2,
        confirmadoPorId: 'actor',
      },
    ],
    pedidos: [
      {
        id: 'p1',
        numeroPedido: 'DEMO-P1',
        clienteId: 'cli',
        clienteNombre: 'DEMO cliente',
        sucursalId: 'mid',
        esExportacion: true,
        estado: 'Importado',
        versionOrigen: 2,
        lineas: [],
        facturaFolio: null,
        facturaUuid: null,
      },
    ],
  };
});
describe('Contenedores de exportación', () => {
  it('muestra partidas liberadas como historial y no permite mutarlas', () => {
    state.data!.partidas[0].estado = 3;
    render(<ContenedoresPanel data={{ ...data, expediente: { ...data.expediente, estado: 2 } }} />);
    expect(screen.getByText('Liberada por cancelación del expediente')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirmar partida' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Quitar partida' })).not.toBeInTheDocument();
  });
  it('muestra revisión A+W y confirma la partida con la acción específica', async () => {
    render(<ContenedoresPanel data={data} />);
    expect(screen.getByText('Revisión: pedido modificado en A+W')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar partida' }));
    await waitFor(() =>
      expect(state.mutar).toHaveBeenCalledWith({ ruta: 'partidas/partida1/confirmar' }),
    );
  });
  it('selector conserva unidad y muestra pendiente acumulado entre contenedores', async () => {
    render(<ContenedoresPanel data={data} />);
    fireEvent.change(screen.getByLabelText('Cliente del expediente', { exact: false }), {
      target: { value: 'cli' },
    });
    fireEvent.change(screen.getByLabelText('Pedido de exportación', { exact: false }), {
      target: { value: 'p1' },
    });
    fireEvent.change(screen.getByLabelText(/Partida del pedido/), { target: { value: '1' } });
    expect(screen.getByText(/Pendiente por asignar: 40 H87/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Cantidad (H87)', { exact: false }), {
      target: { value: '40' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Asignar cantidad' }));
    await waitFor(() =>
      expect(state.mutar).toHaveBeenCalledWith({
        ruta: 'contenedores/c1/partidas',
        body: { pedidoFacturableId: 'p1', posicion: 1, cantidad: 40 },
      }),
    );
  });
  it('sin permiso conserva consulta y oculta gestión', () => {
    useAuthStore.setState({ permisos: [] });
    render(<ContenedoresPanel data={data} />);
    expect(screen.getByText('DEMO-C1', { selector: 'td' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar contenedor' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirmar partida' })).not.toBeInTheDocument();
  });
  it('abre sheet con datos reales y guarda el contenedor', async () => {
    render(<ContenedoresPanel data={data} />);
    fireEvent.click(screen.getByRole('button', { name: 'Agregar contenedor' }));
    fireEvent.change(screen.getByLabelText('Número de contenedor'), {
      target: { value: 'DEMO-C2' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar contenedor' }));
    await waitFor(() =>
      expect(state.mutar).toHaveBeenCalledWith(
        expect.objectContaining({
          ruta: 'contenedores',
          body: expect.objectContaining({ numero: 'DEMO-C2' }),
        }),
      ),
    );
  });
  it('copia de factura muestra BL y pedimento sin captura manual', () => {
    render(
      <EmbarqueCeResumen
        datos={{
          folioExpediente: 'CE-EXP-DEMO',
          numeroPedimento: 'DEMO-PED',
          clavePedimento: 'RT',
          fechaPedimento: '2026-10-12',
          incoterm: 'DDP',
          contenedores: [{ numero: 'DEMO-C1', blGuia: 'DEMO-BL', fechaEmbarqueReal: '2026-10-12' }],
        }}
      />,
    );
    expect(screen.getByText('CE-EXP-DEMO')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'DEMO-BL' })).toBeInTheDocument();
    expect(screen.getByText('RT · Sin CCE.')).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });
});
