import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ConciliacionDiariaPage } from '@/features/almacen/pages/ConciliacionDiariaPage';
import type { CorridaConciliacion, DiferenciaConciliacion } from '@/features/almacen/api/useConciliacionDiaria';

const hook = vi.hoisted(() => ({ aclarar: vi.fn(), ejecutar: vi.fn() }));
const permiso = vi.hoisted(() => ({ tiene: true }));
const datos = vi.hoisted(() => ({ difs: [] as unknown[], corridas: [] as unknown[], existencia: [] as unknown[] }));

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permiso.tiene }));
vi.mock('@/features/almacen/api/useAlmacenes', () => ({ useAlmacenes: () => ({ data: { items: [{ id: 'a-1', clave: 'MP-CNK', nombre: 'Materia prima de prueba' }] } }) }));
vi.mock('@/features/almacen/api/useConciliacionDiaria', () => ({
  useExistenciaConciliacion: () => ({ data: datos.existencia, isLoading: false, isError: false }),
  useDiferenciasConciliacion: () => ({ data: datos.difs, isSuccess: true, isLoading: false, isError: false }),
  useCorridasConciliacion: () => ({ data: datos.corridas, isSuccess: true, isLoading: false, isError: false }),
  useEjecutarConciliacion: () => ({ mutate: hook.ejecutar, isPending: false }),
  useAclararDiferencia: () => ({ mutate: hook.aclarar, isPending: false }),
}));

const falta: DiferenciaConciliacion = {
  id: 'd-1', conciliacionId: 'c-1', almacenId: 'a-1', almacenClave: 'MP-CNK', fecha: '2026-11-04',
  articuloId: 'art-1', articuloClave: 'FL6', articuloDescripcion: 'Flotado claro 6 mm',
  productoAwVarianteId: 'v-1', medida: '2440 x 3660 mm', lote: 'L-2611', unidad: 'hojas',
  teorico: 10, fisico: 9, diferencia: -1, sentido: 'FALTANTE', costoUnitarioMxn: 2232.6, valorMxn: 2232.6,
  folioMovimiento: 'M-SNP2026-000001', estado: 'PENDIENTE', motivoAclaracion: null, aclaradaAt: null,
};
const limpia: CorridaConciliacion = {
  id: 'c-2', almacenId: 'a-1', almacenClave: 'MP-CNK', fecha: '2026-11-03', fuente: 'AW',
  lineasComparadas: 12, diferencias: 0, ejecutadaAt: '2026-11-03T23:30:00Z', detalle: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  permiso.tiene = true;
  datos.difs = [falta];
  datos.corridas = [limpia];
  datos.existencia = [];
});

describe('ConciliacionDiariaPage', () => {
  it('la salida no planificada queda por aclarar y aclararla exige el motivo de la mesa de corte', () => {
    render(<ConciliacionDiariaPage />, { wrapper: createQueryWrapper() });
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Por aclarar/ }));
    expect(screen.getByText('M-SNP2026-000001')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Aclarar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Guardar aclaración' }));
    expect(hook.aclarar).not.toHaveBeenCalled();
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Hoja quebrada en la mesa HEGLA1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar aclaración' }));
    expect(hook.aclarar).toHaveBeenCalledWith({ id: 'd-1', motivo: 'Hoja quebrada en la mesa HEGLA1' }, expect.anything());
  });

  it('un día sin diferencias queda en las corridas como «Sin diferencias»', () => {
    render(<ConciliacionDiariaPage />, { wrapper: createQueryWrapper() });
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Corridas' }));
    expect(screen.getByText('Sin diferencias')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
  });

  it('sin permiso de crear no se ofrece aclarar', () => {
    permiso.tiene = false;
    render(<ConciliacionDiariaPage />, { wrapper: createQueryWrapper() });
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Por aclarar/ }));
    expect(screen.queryByRole('button', { name: 'Aclarar' })).not.toBeInTheDocument();
  });
});

it.each([0, 1])('el resultado con %s diferencias muestra la fecha formateada y no código de la vista', async (diferencias) => {
  datos.existencia = [falta];
  hook.ejecutar.mockImplementationOnce((_comando: unknown, opciones: { onSuccess: (c: CorridaConciliacion) => void }) => {
    opciones.onSuccess({ ...limpia, fecha: '2026-11-04', diferencias });
  });
  render(<ConciliacionDiariaPage />, { wrapper: createQueryWrapper() });
  fireEvent.keyDown(screen.getByRole('combobox', { name: 'Almacén' }), { key: 'ArrowDown' });
  fireEvent.click(await screen.findByRole('option', { name: /MP-CNK/ }));
  fireEvent.change(screen.getByLabelText('Día a conciliar'), { target: { value: '2026-11-04' } });
  const captura = screen.getByLabelText(/Físico de FL6/);
  fireEvent.change(captura, { target: { value: '9' } });
  fireEvent.blur(captura);
  fireEvent.click(screen.getByRole('button', { name: /^Conciliar/ }));
  const resultado = await screen.findByText(/Corrida del/);
  expect(resultado).toHaveTextContent('4 nov');
  expect(resultado).not.toHaveTextContent(/DateTimeDisplay|2026-11-04|\$</);
  expect(hook.ejecutar).toHaveBeenCalledWith({
    almacenId: 'a-1', fecha: '2026-11-04', fuente: 'AW',
    lineas: [{ articuloId: 'art-1', productoAwVarianteId: 'v-1', lote: 'L-2611', fisico: 9 }],
  }, expect.anything());
});
