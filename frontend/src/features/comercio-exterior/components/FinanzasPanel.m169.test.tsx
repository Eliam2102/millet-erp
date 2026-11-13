import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ExpedienteDetalle } from '../api/types';
import { FinanzasPanel } from './FinanzasPanel';
// M-169: el gasto de importación va a gasto (no al inventario) y el grupo contable se configura por concepto.
const { guardar, finanzas } = vi.hoisted(() => ({
  guardar: vi.fn().mockResolvedValue({}),
  finanzas: { incrementablesAlInventario: false as boolean | undefined },
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
vi.mock('./ReferenciaSelector', () => ({ ReferenciaSelector: () => null }));
vi.mock('../api/hooks', () => ({
  CE_BASE: '/ce',
  useContenedores: () => ({ data: { data: { contenedores: [] } } }),
  useExpedientes: () => ({}),
  useMutarContenedor: () => ({}),
}));
vi.mock('@tanstack/react-query', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tanstack/react-query')>()),
  useMutation: () => ({}),
}));
vi.mock('../api/finanzas', () => ({
  useFinanzas: () => ({
    data: {
      data: {
        pendientes: [],
        estadoFinanciero: 'Abierto',
        nombres: {},
        conceptos: [
          {
            id: 'hon',
            codigo: 'HONORARIOS',
            nombre: 'Honorarios del agente aduanal',
            baseImportacion: 1,
            baseExportacion: 2,
            requiereOrdenServicio: true,
            grupoContable: 1,
          },
        ],
        gastos: [],
        partidas: [],
        bases: [],
        distribuciones: [],
        anticipos: [],
        ordenesServicio: [],
        incrementablesAlInventario: finanzas.incrementablesAlInventario,
      },
      etag: '"1"',
    },
  }),
  useMutarFinanzas: () => ({ mutateAsync: guardar }),
  useDimensionesDistribucion: () => ({ data: undefined }),
  endpointCentrosDistribucion: (id: string) => `/ce/${id}/gastos/distribuir/centros`,
  useOrdenesServicio: () => ({ data: { data: [] } }),
}));
const expediente = (tipo: number) =>
  ({ expediente: { id: 'e', estado: 1, tipo }, participantes: [] }) as unknown as ExpedienteDetalle;
const texto = 'El gasto se registra como gasto de importación; no cambia el costo del inventario.';
beforeEach(() => {
  guardar.mockClear();
  finanzas.incrementablesAlInventario = false;
});
describe('M-169 · gasto de importación por concepto', () => {
  it('muestra el texto fijo en importación y no ofrece repartir a inventario', () => {
    render(<FinanzasPanel data={expediente(1)} tab="gastos" />);
    expect(screen.getByText(texto)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /inventario/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/inventario/i)).not.toBeInTheDocument();
  });
  it('no muestra el texto en exportación ni con el reparto a inventario activado', () => {
    const { unmount } = render(<FinanzasPanel data={expediente(2)} tab="gastos" />);
    expect(screen.queryByText(texto)).not.toBeInTheDocument();
    unmount();
    finanzas.incrementablesAlInventario = true;
    render(<FinanzasPanel data={expediente(1)} tab="gastos" />);
    expect(screen.queryByText(texto)).not.toBeInTheDocument();
  });
  it('guarda el grupo contable del concepto', () => {
    render(<FinanzasPanel data={expediente(1)} tab="gastos" />);
    fireEvent.click(screen.getByText('Configurar bases por concepto'));
    const grupo = screen.getByLabelText('Grupo contable');
    expect(grupo).toHaveValue('1');
    fireEvent.change(grupo, { target: { value: '4' } });
    const form = grupo.closest('form')!;
    fireEvent.click(within(form).getByRole('button', { name: 'Guardar base' }));
    expect(guardar).toHaveBeenCalledWith(
      expect.objectContaining({
        ruta: 'gastos/conceptos/hon',
        method: 'PUT',
        body: expect.objectContaining({ grupoContable: 4 }),
      }),
    );
  });
});
