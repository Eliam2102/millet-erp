import { hoyLocalISO } from '@/lib/datetime';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { DepreciacionPage } from './DepreciacionPage';
import { DepreciacionActivo } from '../components/DepreciacionActivo';
import type { Activo } from '../api/types';
const state = vi.hoisted(() => ({
  mutate: vi.fn(),
  corridas: [] as unknown[],
  operaciones: [] as unknown[],
  error: null as Error | null,
}));
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
  }: {
    children: React.ReactNode;
    to: string;
    params: { id: string };
  }) => <a href={to.replace('$id', params.id)}>{children}</a>,
}));
vi.mock('../api/hooks', () => ({
  useGuardarActivo: () => ({ mutate: state.mutate, isPending: false, error: state.error }),
}));
vi.mock('../api/libro', () => ({
  useLibroQuery: (ruta: string, enabled = true) => ({
    data: !enabled
      ? undefined
      : ruta === 'depreciacion'
        ? state.corridas
        : ruta.includes('/libro')
          ? { depreciaciones: [], operaciones: state.operaciones }
          : [{ numero: 'DEMO-MAQ', importe: 4166.67, tasa: 10 }],
    error: null,
  }),
}));
vi.mock('../components/TablaLibro', () => ({
  TablaLibro: ({
    titulo,
    filas,
    accionFila,
  }: {
    titulo: string;
    filas?: unknown[];
    accionFila?: (r: unknown) => React.ReactNode;
  }) => (
    <section aria-label={titulo}>
      {filas?.map((f, i) => (
        <div key={i}>{accionFila?.(f)}</div>
      ))}
    </section>
  ),
}));
vi.mock('@/components/erp/PageHeader', () => ({
  PageHeader: ({ titulo }: { titulo: string }) => <h1>{titulo}</h1>,
}));
describe('Libro de activos P4', () => {
  beforeEach(() => {
    state.corridas = [];
    state.operaciones = [];
    state.error = null;
    state.mutate.mockReset();
    useAuthStore.setState({ permisos: [P.ActivosFijosDepreciacionCorrer] });
  });
  it('exige vista previa y envía el periodo consultado', () => {
    render(<DepreciacionPage />);
    expect(screen.getByLabelText('Periodo *')).toHaveValue(hoyLocalISO().slice(0, 7));
    expect(screen.getByRole('button', { name: 'Confirmar corrida' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Vista previa' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar corrida' }));
    expect(state.mutate).toHaveBeenCalledWith({
      ruta: 'depreciacion',
      body: { periodo: `${hoyLocalISO().slice(0, 7)}-01` },
    });
    fireEvent.change(screen.getByLabelText('Periodo *'), {
      target: { value: hoyLocalISO().startsWith('2099-01') ? '2099-02' : '2099-01' },
    });
    expect(screen.getByRole('button', { name: 'Confirmar corrida' })).toBeDisabled();
  });
  it('impide confirmar de nuevo y exige motivo al anular', () => {
    state.corridas = [
      {
        id: 'corrida',
        periodo: `${hoyLocalISO().slice(0, 7)}-01`,
        version: 3,
        revision: 1,
        anulada: false,
        detalles: [],
      },
    ];
    useAuthStore.setState({
      permisos: [P.ActivosFijosDepreciacionCorrer, P.ActivosFijosDepreciacionAnular],
    });
    render(<DepreciacionPage />);
    expect(screen.getByText('Ya confirmada')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Anular corrida' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo de anulación *'), {
      target: { value: 'Corrección de base' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Anular corrida' }));
    expect(state.mutate).toHaveBeenCalledWith({
      ruta: 'depreciacion/corrida/anular',
      version: 3,
      body: { motivo: 'Corrección de base' },
    });
  });
  it.each([true, false])('consulta la póliza de depreciación según permiso: %s', (permitido) => {
    state.corridas = [
      {
        id: 'corrida',
        periodo: `${hoyLocalISO().slice(0, 7)}-01`,
        version: 1,
        detalles: [],
        polizaId: 'poliza-depreciacion',
      },
    ];
    useAuthStore.setState({ permisos: permitido ? [P.ContabilidadPolizasLeer] : [] });
    render(<DepreciacionPage />);
    if (permitido)
      expect(screen.getByRole('link', { name: 'Ver póliza' })).toHaveAttribute(
        'href',
        '/contabilidad/polizas/poliza-depreciacion',
      );
    else expect(screen.queryByRole('link', { name: 'Ver póliza' })).not.toBeInTheDocument();
  });
  it('oculta baja y ajuste sin permiso, y exige adjunto al solicitar baja', () => {
    const activo = { id: 'a', estado: 1, version: 4 } as Activo;
    const { unmount } = render(<DepreciacionActivo activo={activo} />);
    expect(screen.queryByRole('button', { name: 'Dar de baja' })).not.toBeInTheDocument();
    unmount();
    useAuthStore.setState({ permisos: [P.ActivosFijosBajasRegistrar] });
    render(<DepreciacionActivo activo={activo} />);
    fireEvent.click(screen.getByRole('button', { name: 'Dar de baja' }));
    expect(screen.getByRole('button', { name: 'Solicitar autorización' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Ajuste' })).not.toBeInTheDocument();
  });
  it('expone el rechazo contable', () => {
    state.error = new Error('El periodo no está abierto en Contabilidad.');
    render(<DepreciacionPage />);
    expect(screen.getByRole('alert')).toHaveTextContent('El periodo no está abierto');
  });
});
