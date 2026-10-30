import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PropuestaReposicion } from './PropuestaReposicion';
const mocks = vi.hoisted(() => ({ mutate: vi.fn() }));
vi.mock('@/features/almacen/api/useReorden', () => ({
  usePropuestaReorden: () => ({ isLoading: false, refetch: vi.fn(), data: [
    { configuracionId: 'c1', articuloId: 'a1', nivel: 1, entidadId: 'e1', existenciaFisica: 8, vivo: 0, faltante: 32, puntoReorden: 10,
      minimo: 5, maximo: 40, consumoDiario: 1, coberturaDias: 8, minimoSugerido: 7, maximoSugerido: 37 },
  ] }),
  useEditarReorden: () => ({ mutate: mocks.mutate, isPending: false }),
}));
const items = [{ id: 'c1', articuloId: 'a1', nivel: 1, entidadId: 'e1', minimo: 5, maximo: 40, cantidadFija: null, puntoReorden: 10,
  autoRequisicion: true, objetivo: 1, estatus: 0, articuloClave: 'ART1', articuloDescripcion: 'Artículo uno' }] as never;
describe('PropuestaReposicion', () => {
  beforeEach(() => mocks.mutate.mockClear());
  it('muestra consumo y cobertura sin cambiar parámetros hasta aplicar', () => {
    render(<PropuestaReposicion items={items} puedeAdministrar />);
    expect(screen.getByText('ART1 Artículo uno')).toBeInTheDocument();
    expect(screen.getByText('8.0')).toBeInTheDocument();
    expect(mocks.mutate).not.toHaveBeenCalled();
  });
  it('aplicar sugerencia edita con mínimo y máximo sugeridos y conserva el resto', () => {
    render(<PropuestaReposicion items={items} puedeAdministrar />);
    fireEvent.click(screen.getByRole('button', { name: 'Aplicar sugerencia' }));
    expect(mocks.mutate.mock.calls[0][0].command).toMatchObject({ id: 'c1', minimo: 7, maximo: 37, puntoReorden: 10, autoRequisicion: true });
  });
  it('sin permiso de administrar no ofrece aplicar', () => {
    render(<PropuestaReposicion items={items} puedeAdministrar={false} />);
    expect(screen.queryByRole('button', { name: 'Aplicar sugerencia' })).toBeNull();
  });
});
