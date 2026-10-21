import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ConteosPage } from './ConteosPage';
import { PorVencerPage } from './PorVencerPage';
import type { Conteo } from '../api/conservacion';
const state = vi.hoisted(() => ({
  gestionar: true,
  autorizar: true,
  mutate: vi.fn(),
  conteo: null as Conteo | null,
  rutas: [] as string[],
}));
vi.mock('../api/hooks', () => ({
  useGuardarActivo: () => ({ mutate: state.mutate, isPending: false, error: null }),
}));
vi.mock('../api/conservacion', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  useConservacion: (ruta: string, enabled = true) => {
    if (!enabled) return { data: undefined, error: null, isPending: false };
    state.rutas.push(ruta);
    if (ruta.startsWith('conteos/')) return { data: state.conteo, error: null, isPending: false };
    if (ruta.startsWith('por-vencer'))
      return {
        data: {
          hoy: '2026-10-20',
          items: [
            {
              id: 'd',
              activoId: 'a',
              numero: 'SAP-1',
              descripcion: 'Equipo',
              referencia: 'Garantía',
              vence: '2026-11-09',
              esMantenimiento: false,
            },
          ],
          total: 1,
        },
        error: null,
        isPending: false,
      };
    return {
      data: { items: state.conteo ? [state.conteo] : [], total: state.conteo ? 1 : 0 },
      error: null,
      isPending: false,
    };
  },
}));
vi.mock('@/lib/auth/useHasPermission', () => ({
  useHasPermission: (p: string) => (p.endsWith('autorizar') ? state.autorizar : state.gestionar),
}));
vi.mock('@/lib/auth/auth-store', () => ({
  useAuthStore: (selector: (s: unknown) => unknown) => selector({ user: { id: 'contador' } }),
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: ({
    onChange,
    value,
    disabled,
  }: {
    onChange: (v: string) => void;
    value: string;
    disabled: boolean;
  }) => (
    <select
      aria-label="Sucursal"
      disabled={disabled}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
    >
      <option value="">Todas</option>
      <option value="conkal">Conkal</option>
    </select>
  ),
}));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
const conteo = (): Conteo => ({
  id: 'c',
  version: 7,
  sucursalId: 'conkal',
  ubicacion: 'Nave A',
  createdAt: '2026-10-20T12:00:00Z',
  cerradoEn: null,
  partidas: [
    {
      id: 'p',
      numero: 'SAP-1',
      descripcionEsperada: 'Equipo',
      ubicacionEsperada: 'Nave A',
      resultado: 0,
      ubicacionReal: null,
      evidencia: null,
      capturadoPor: null,
      autorizadoPor: null,
      evidenciaAutorizacion: null,
      propuesta: null,
    },
  ],
});
beforeEach(() => {
  state.mutate.mockClear();
  state.gestionar = true;
  state.autorizar = true;
  state.conteo = conteo();
  state.rutas = [];
});
describe('Conservación de activos', () => {
  it('permite escanear el número y envía resultado y versión vigente', () => {
    render(<ConteosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Abrir reporte' }));
    const numero = screen.getByLabelText('Escanear o escribir número *');
    fireEvent.change(numero, { target: { value: 'SAP-1' } });
    fireEvent.keyDown(numero, { key: 'Enter' });
    expect(screen.getByLabelText('Evidencia u observación *')).toHaveFocus();
    fireEvent.change(screen.getByLabelText('Evidencia u observación *'), {
      target: { value: 'Placa verificada' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar resultado' }));
    expect(state.mutate).toHaveBeenCalledWith(
      expect.objectContaining({
        ruta: 'conteos/c/capturar',
        version: 7,
        body: {
          captura: {
            numero: 'SAP-1',
            resultado: 1,
            ubicacionReal: null,
            evidencia: 'Placa verificada',
          },
        },
      }),
      expect.anything(),
    );
    expect(screen.getByRole('button', { name: 'Cerrar conteo' })).toBeDisabled();
  });
  it('un conteo cerrado oculta captura y bloquea autorizar la propia diferencia', () => {
    state.conteo!.cerradoEn = '2026-10-20T13:00:00Z';
    state.conteo!.partidas[0].resultado = 2;
    state.conteo!.partidas[0].capturadoPor = 'contador';
    render(<ConteosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Abrir reporte' }));
    expect(screen.queryByRole('button', { name: 'Registrar resultado' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autorizar diferencia' })).toBeDisabled();
  });
  it('otra persona puede preparar autorización con evidencia', () => {
    state.conteo!.cerradoEn = '2026-10-20T13:00:00Z';
    state.conteo!.partidas[0].resultado = 3;
    state.conteo!.partidas[0].capturadoPor = 'otra-persona';
    render(<ConteosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Abrir reporte' }));
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar diferencia' }));
    fireEvent.change(screen.getByLabelText('Evidencia de autorización *'), {
      target: { value: 'Verificado en nave B' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar propuesta' }));
    expect(state.mutate).toHaveBeenCalledWith(
      expect.objectContaining({
        ruta: 'conteos/c/autorizar',
        version: 7,
        body: { partidaId: 'p', evidencia: 'Verificado en nave B' },
      }),
      expect.anything(),
    );
  });
  it('oculta abrir conteo sin permiso y filtra vencimientos por sucursal', () => {
    state.gestionar = false;
    const vista = render(<ConteosPage />);
    expect(screen.queryByRole('button', { name: 'Abrir conteo' })).not.toBeInTheDocument();
    vista.unmount();
    render(<PorVencerPage />);
    expect(screen.getByText('Garantía')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Sucursal'), { target: { value: 'conkal' } });
    expect(state.rutas.at(-1)).toContain('sucursalId=conkal');
  });
});
