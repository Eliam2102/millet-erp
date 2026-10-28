import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ActivoDetallePage } from './ActivoDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
const state = vi.hoisted(() => ({
  mutate: vi.fn(),
  activo: {
    id: 'a',
    numero: 'AF-DEMO',
    descripcion: 'Activo sintético',
    estado: 1,
    version: 3,
    valorOriginal: 100,
    valorActual: 100,
    valorEnLibros: 100,
    depreciacionAcumulada: 0,
    responsableId: 'r',
    sucursalId: 's',
    ubicacion: 'Origen',
    centroCostoId: 'c',
    equipoId: null,
    fechaAdquisicion: '2026-01-01',
    inicioUso: '2026-01-15',
    moneda: 'MXN',
    valorMonedaOriginal: 100,
    tipoCambio: 1,
    historial: [],
    enTraslado: false,
    traslados: [
      {
        id: 't',
        fechaEfectiva: '2026-02-15',
        motivo: 'Reubicación de prueba',
        registradoPor: 'actor',
        registradoPorNombre: 'Contador DEMO',
        registradoEn: '2026-02-16T12:00:00Z',
        aceptadoEn: '2026-02-16T12:00:00Z',
        origen: { ubicacion: 'Nave A' },
        destino: { ubicacion: 'Nave B' },
        origenDescripcion: { sucursal: 'Conkal', responsable: 'Ana', centroCosto: 'Centro A' },
        destinoDescripcion: { sucursal: 'Conkal', responsable: 'Luis', centroCosto: 'Centro B' },
      },
    ],
  },
}));
vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'a' }),
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
vi.mock('../api/hooks', () => ({
  useActivo: () => ({ data: { data: state.activo } }),
  useActivos: () => ({ data: { items: [] } }),
  useClasesActivo: () => ({ data: [] }),
  useGuardarActivo: () => ({ mutate: state.mutate, isPending: false }),
}));
vi.mock('../components/DepreciacionActivo', () => ({ DepreciacionActivo: () => null }));
vi.mock('../components/ImprimirEtiquetas', () => ({ ImprimirEtiquetas: () => null }));
vi.mock('../components/DocumentosActivo', () => ({ DocumentosActivo: () => null }));
vi.mock('../components/MantenimientosActivo', () => ({ MantenimientosActivo: () => null }));
vi.mock('../components/DocumentoProveedorSelector', () => ({
  DocumentoProveedorSelector: () => null,
}));
vi.mock('../components/ActivoForm', () => ({
  ActivoForm: () => null,
  CustodiaFields: ({ onChange }: { onChange: (value: unknown) => void }) => (
    <button
      type="button"
      onClick={() =>
        onChange({
          responsableId: 'r',
          sucursalId: 's',
          centroCostoId: 'c',
          equipoId: null,
          ubicacion: 'Destino',
        })
      }
    >
      Elegir destino
    </button>
  ),
}));
beforeEach(() => {
  state.mutate.mockClear();
  useAuthStore.setState({ permisos: [P.ActivosFijosActivosVer, P.ActivosFijosTrasladosGestionar] });
});
async function historial() {
  fireEvent.mouseDown(screen.getByRole('tab', { name: 'Traslados' }), {
    button: 0,
    ctrlKey: false,
  });
  fireEvent.click(screen.getByRole('tab', { name: 'Traslados' }));
  return screen.findByText(/Reubicación de prueba/);
}
it('muestra fecha efectiva, nombres, motivo y registrador sin aceptación', async () => {
  render(<ActivoDetallePage />);
  await historial();
  expect(screen.getByText(/2026-02-15/)).toBeInTheDocument();
  expect(screen.getByText(/Contador DEMO/)).toBeInTheDocument();
  expect(screen.getByText(/Centro A/)).toHaveTextContent('Centro B');
  expect(screen.queryByRole('button', { name: /Aceptar/ })).not.toBeInTheDocument();
});
it('exige motivo y envía el traslado directo con fecha efectiva y versión', async () => {
  render(<ActivoDetallePage />);
  await historial();
  fireEvent.click(screen.getByRole('button', { name: 'Trasladar' }));
  const motivo = screen.getByLabelText('Motivo *') as HTMLInputElement;
  expect(motivo).toBeRequired();
  expect(motivo.maxLength).toBe(300);
  expect(motivo.checkValidity()).toBe(false);
  fireEvent.change(motivo, { target: { value: '   ' } });
  expect(motivo.checkValidity()).toBe(false);
  fireEvent.change(motivo, { target: { value: 'Nueva ubicación' } });
  fireEvent.change(screen.getByLabelText('Fecha efectiva *'), { target: { value: '2026-03-15' } });
  fireEvent.click(screen.getByRole('button', { name: 'Elegir destino' }));
  fireEvent.click(screen.getByRole('button', { name: 'Registrar traslado' }));
  expect(state.mutate).toHaveBeenCalledWith(
    expect.objectContaining({
      ruta: 'activos/a/trasladar',
      version: 3,
      body: expect.objectContaining({
        motivo: 'Nueva ubicación',
        fechaEfectiva: '2026-03-15',
        destino: expect.objectContaining({ ubicacion: 'Destino' }),
      }),
    }),
    expect.anything(),
  );
});
it('oculta traslado sin permiso', async () => {
  useAuthStore.setState({ permisos: [P.ActivosFijosActivosVer] });
  render(<ActivoDetallePage />);
  await historial();
  expect(screen.queryByRole('button', { name: 'Trasladar' })).not.toBeInTheDocument();
});
