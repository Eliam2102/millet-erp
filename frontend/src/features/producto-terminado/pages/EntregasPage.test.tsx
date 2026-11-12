import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { EntregasPage } from './EntregasPage';

const state = vi.hoisted(() => ({ lista: vi.fn(), detalle: vi.fn(), evidencia: vi.fn() }));

vi.mock('./TramoDetenidoBanner', () => ({ TramoDetenidoBanner: () => null }));
vi.mock('../lib/usuarios', () => ({ useNombreUsuario: () => (id?: string | null) => id ?? '—' }));
vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  Breadcrumbs: () => null,
}));
vi.mock('@/features/catalogos/api', () => ({ useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Conkal' }] } }) }));
vi.mock('./EntregaPanel', () => ({ EntregaPanel: ({ albaran }: { albaran: { numeroPedido: string } }) => <div>panel {albaran.numeroPedido}</div> }));
vi.mock('../api/hooks', () => ({ useHojasSalida: state.lista, useHojaSalida: state.detalle, useEvidenciaEntrega: state.evidencia }));

const hoja = { id: 'h1', folio: 'HS-20261105-A1B2C', sucursalId: 'suc-1', destino: 'Mérida norte', estado: 'Emitida', creadaAt: '2026-11-05T12:00:00Z', emitidaAt: '2026-11-05T12:05:00Z', pedidos: 2, piezas: 5, entregasPendientes: 1 };
const albaran = (id: string, numero: string, extra = {}) => ({ id, folio: `HS/${id}`, secuencia: 1, numeroPedido: numero, cliente: 'Constructora Peninsular', destino: null, emitidoAt: null, total: 2, lineas: [{ posicion: 1, descripcion: 'Templado 6 mm', piezas: 2 }], etiquetas: ['E1', 'E2'], ...extra });
const entregada = { id: 'e1', resultado: 'Parcial', receptor: 'Mariana Pech', entregadaAt: '2026-11-05T15:00:00Z', entregadas: 1, rechazadas: 1, motivoRechazo: 'Vidrio con rayón', tieneFirma: true, tieneFoto: false, etiquetasRechazadas: ['E2'], cobro: { monto: 8500, formaPago: 'TPV', referencia: 'VCH-1', conciliadoAt: null } };

beforeEach(() => {
  vi.clearAllMocks();
  state.lista.mockReturnValue({ data: { items: [hoja], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
  state.detalle.mockReturnValue({ data: { ...hoja, albaranes: [albaran('a1', '260317'), albaran('a2', '260318', { entrega: entregada, estadoPedido: '71' })] } });
  state.evidencia.mockReturnValue({ data: { firma: 'data:image/png;base64,AAAA', foto: null }, isLoading: false });
  useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar] });
});

describe('EntregasPage', () => {
  it('lista las rutas emitidas con lo que falta por entregar y abre cada pedido', () => {
    render(<EntregasPage />);
    expect(screen.getByText('1 por entregar')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ver entregas' }));
    expect(screen.getByText('Por entregar')).toBeInTheDocument();
    expect(screen.getByText('Entrega parcial')).toBeInTheDocument();
    expect(screen.getByText("En ruta")).toBeInTheDocument();
    expect(screen.getByText(/Rechazadas en sitio \(E2\): Vidrio con rayón/)).toBeInTheDocument();
    expect(screen.getByText(/pendiente de liquidar en caja/)).toBeInTheDocument();
  });

  it('confirmar abre el panel del pedido y la evidencia se ve bajo demanda', () => {
    render(<EntregasPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver entregas' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar entrega' }));
    expect(screen.getByText('panel 260317')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ver evidencia' }));
    expect(screen.getByAltText('Firma de quien recibió')).toBeInTheDocument();
  });

  it('sin el permiso de operar no se ofrece confirmar', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer] });
    render(<EntregasPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver entregas' }));
    expect(screen.queryByRole('button', { name: 'Confirmar entrega' })).not.toBeInTheDocument();
    expect(screen.getByText('Sin permiso para confirmar entregas.')).toBeInTheDocument();
  });
});
