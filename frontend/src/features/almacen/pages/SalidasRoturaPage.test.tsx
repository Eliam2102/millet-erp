import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { SalidasRoturaPage } from '@/features/almacen/pages/SalidasRoturaPage';
import type { SalidaRotura } from '@/features/almacen/api/useSalidasRotura';

const hook = vi.hoisted(() => ({ useSalidasRotura: vi.fn(), mutate: vi.fn() }));
const permisos = vi.hoisted(() => ({ activos: new Set<string>() }));

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: (p: string) => permisos.activos.has(p) }));
vi.mock('@/features/almacen/components/CapturarRoturaSheet', () => ({
  CapturarRoturaSheet: ({ open }: { open: boolean }) => (open ? <div>Formulario de captura</div> : null),
}));
vi.mock('@/features/almacen/api/useSalidasRotura', () => ({
  useSalidasRotura: hook.useSalidasRotura,
  useDecidirRotura: () => ({ mutate: hook.mutate, isPending: false }),
}));

const pendiente: SalidaRotura = {
  id: 'r-1',
  sucursalId: 's-1',
  ubicacionId: 'u-1',
  ubicacionClave: 'R-01',
  articuloId: 'a-1',
  articuloClave: 'VCL06',
  articuloDescripcion: 'Vidrio claro 6 mm',
  cantidad: 2,
  unidadMedida: 'PZA',
  fecha: '2026-11-05',
  motivoCodigo: 'rotura_rack',
  motivo: 'Rotura en rack',
  comentario: null,
  estado: 'PENDIENTE',
  capturadoPor: 'Mauricio Canul',
  capturadoAt: '2026-11-05T15:00:00Z',
  decididoPor: null,
  decididoAt: null,
  motivoRechazo: null,
  movimientoId: null,
  folioMovimiento: null,
  costoUnitarioMxn: null,
  valorMxn: null,
};
const autorizada: SalidaRotura = {
  ...pendiente,
  id: 'r-2',
  estado: 'AUTORIZADA',
  decididoPor: 'Fernando Alejos',
  decididoAt: '2026-11-05T16:00:00Z',
  folioMovimiento: 'M-ROT2026-000001',
  costoUnitarioMxn: 900,
  valorMxn: 1800,
};

function datos(items: SalidaRotura[]) {
  hook.useSalidasRotura.mockReturnValue({ data: items, isSuccess: true, isLoading: false, isError: false, refetch: vi.fn() });
}
const dar = (...p: string[]) => p.forEach((x) => permisos.activos.add(x));

beforeEach(() => {
  vi.clearAllMocks();
  permisos.activos.clear();
});

describe('SalidasRoturaPage', () => {
  it('lista la rotura pendiente con cantidad, ubicación, motivo y quién la capturó', () => {
    datos([pendiente]);
    render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText('R-01')).toBeInTheDocument();
    expect(screen.getByText('Rotura en rack')).toBeInTheDocument();
    expect(screen.getByText('Mauricio Canul')).toBeInTheDocument();
    expect(screen.getByText('Pendiente de autorizar')).toBeInTheDocument();
  });

  it('sin permisos no ofrece capturar ni autorizar y se explica', () => {
    datos([pendiente]);
    render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: /Capturar rotura/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
    expect(screen.getByText('Requiere autorización')).toBeInTheDocument();
  });

  it('con permiso de captura abre el formulario', () => {
    dar('almacen.salidas.rotura-capturar');
    datos([]);
    render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /Capturar rotura/ }));
    expect(screen.getByText('Formulario de captura')).toBeInTheDocument();
  });

  it('con facultad Autorizar pide confirmación y manda la decisión', () => {
    dar('almacen.salidas.rotura-autorizar');
    datos([pendiente]);
    render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar rotura' }));
    expect(hook.mutate).toHaveBeenCalledWith({ id: 'r-1', accion: 'autorizar', motivo: undefined }, expect.anything());
  });

  it('rechazar sin motivo no manda la decisión', () => {
    dar('almacen.salidas.rotura-autorizar');
    datos([pendiente]);
    render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar rotura' }));
    expect(hook.mutate).not.toHaveBeenCalled();
  });

  it('el detalle de una autorizada muestra el folio del movimiento y la bitácora', () => {
    datos([autorizada]);
    render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Autorizadas/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Detalle' }));
    expect(screen.getByText('Bitácora')).toBeInTheDocument();
    expect(screen.getByText('Fernando Alejos')).toBeInTheDocument();
    expect(screen.getAllByText('M-ROT2026-000001').length).toBeGreaterThan(0);
  });
});

  it.each(['cargando', 'error'])('los indicadores no inventan ceros mientras están %s', (estado) => {
    hook.useSalidasRotura.mockReturnValue({ data: undefined, isSuccess: false, isLoading: estado === 'cargando', isError: estado === 'error', refetch: vi.fn() });
    const { container } = render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    const valores = container.querySelectorAll('span.text-3xl');
    expect(valores).toHaveLength(3);
    expect(screen.getByRole('tab', { name: 'Pendientes' })).toBeInTheDocument();
    valores.forEach((valor) => expect(valor).toHaveTextContent('—'));
  });
  it('los indicadores muestran cero cuando ya cargaron sin registros', () => {
    datos([]);
    const { container } = render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
    expect(container.querySelectorAll('span.text-3xl')[0]).toHaveTextContent('0');
    expect(container.querySelectorAll('span.text-3xl')[1]).toHaveTextContent('0');
    const importe = container.querySelectorAll('span.text-3xl')[2];
    expect(importe).toHaveClass('text-3xl', 'font-semibold', 'tabular-nums');
    expect(importe.querySelector('.font-medium')).toBeNull();
  });

it('la decisión rechazada conserva su nombre y usa el tono de rechazo', () => {
  datos([{ ...pendiente, estado: 'RECHAZADA' }]);
  render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
  fireEvent.mouseDown(screen.getByRole('tab', { name: /^Rechazadas/ }));
  expect(screen.getByText('Rechazada')).toHaveClass('bg-danger-bg', 'text-danger-fg');
});

it('no calcula un total de cero si una rotura autorizada no trae su valor', () => {
  datos([{ ...autorizada, valorMxn: null }]);
  const { container } = render(<SalidasRoturaPage />, { wrapper: createQueryWrapper() });
  const importe = container.querySelectorAll('span.text-3xl')[2];
  expect(importe).toHaveTextContent(/^—$/);
  expect(importe.querySelector('.text-ink-muted')).toBeInTheDocument();
});
