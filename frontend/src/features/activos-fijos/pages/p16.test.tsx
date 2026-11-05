import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { DepreciacionPage } from './DepreciacionPage';
import { VersionesClase } from '../components/VersionesClase';
import type { Clase } from '../api/types';
const state = vi.hoisted(() => ({
  mutate: vi.fn(),
  tasa: null as number | null,
  corridas: [] as unknown[],
}));
vi.mock('@/features/contabilidad/api/hooks', () => ({ useCuenta: () => ({ data: null }) }));
vi.mock('../api/hooks', () => ({
  useGuardarActivo: () => ({ mutate: state.mutate, isPending: false }),
}));
vi.mock('../api/libro', () => ({
  useLibroQuery: (ruta: string, enabled = true) => ({
    data: enabled
      ? ruta === 'depreciacion'
        ? state.corridas
        : [
            {
              activoId: 'a',
              numero: 'ACF-SIN-TASA',
              tasa: state.tasa,
              observacion: state.tasa == null ? 'Capture la tasa' : 'Tasa por confirmar con Millet',
            },
          ]
      : undefined,
  }),
}));
vi.mock('../components/TablaLibro', () => ({
  TablaLibro: ({
    filas,
    accionFila,
  }: {
    filas?: unknown[];
    accionFila?: (fila: unknown) => React.ReactNode;
  }) => (
    <div>
      {filas?.map((f, i) => (
        <div key={i}>{accionFila?.(f)}</div>
      ))}
    </div>
  ),
}));
beforeEach(() => {
  state.mutate.mockReset();
  state.tasa = null;
  state.corridas = [];
  useAuthStore.setState({
    permisos: [P.ActivosFijosDepreciacionCorrer, P.ActivosFijosClasesAutorizar],
    user: { id: 'otro', nombre: 'Prueba', email: 'prueba@example.invalid' },
  });
});
it('bloquea sin tasa y nombra el activo', () => {
  render(<DepreciacionPage />);
  fireEvent.click(screen.getByRole('button', { name: 'Vista previa' }));
  expect(screen.getByRole('alert')).toHaveTextContent('ACF-SIN-TASA');
  expect(screen.getByRole('button', { name: 'Confirmar corrida' })).toBeDisabled();
});
it('la tasa pendiente de confirmar sólo avisa', () => {
  state.tasa = 10;
  render(<DepreciacionPage />);
  fireEvent.click(screen.getByRole('button', { name: 'Vista previa' }));
  expect(screen.getByText(/Tasa por confirmar con Millet/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Confirmar corrida' })).toBeEnabled();
});
it('ofrece recuperación con la versión de la corrida', () => {
  state.corridas = [
    { id: 'c', version: 3, periodo: '2026-02-01', detalles: [], sinEfectoContable: true },
  ];
  render(<DepreciacionPage />);
  expect(screen.getByText('Sin efecto contable')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Generar póliza de nuevo' }));
  expect(state.mutate).toHaveBeenCalledWith({
    ruta: 'depreciacion/c/recuperar-poliza',
    version: 3,
    body: {},
  });
});
it('autoriza o rechaza la propuesta con motivo', () => {
  const clase = {
    id: 'c',
    clave: '153',
    versiones: [
      {
        id: 'v',
        version: 2,
        numero: 2,
        vigencia: '2027-03-01',
        estado: 0,
        propuestoPor: 'proponente',
        tasaContableAnual: 25,
        tasaFiscalAnual: 10,
      },
    ],
  } as Clase;
  render(<VersionesClase clase={clase} />);
  expect(screen.getByRole('button', { name: 'Rechazar versión' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Motivo de rechazo'), {
    target: { value: 'Conservar la anterior' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Rechazar versión' }));
  expect(state.mutate).toHaveBeenCalledWith({
    ruta: 'clases/c/versiones/v/resolver',
    version: 2,
    body: { autorizar: false, motivo: 'Conservar la anterior' },
  });
});
