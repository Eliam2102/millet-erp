import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ResolverOperacion } from '../components/ResolverOperacion';
import { ReversaActivo } from '../components/ReversaActivo';
import { ResolverAviso } from '../components/ResolverAviso';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import type { Activo } from '../api/types';
import type { Operacion } from '../api/libro';
const state = vi.hoisted(() => ({ mutate: vi.fn() }));
vi.mock('../api/hooks', () => ({
  useGuardarActivo: () => ({ mutate: state.mutate, isPending: false }),
}));
vi.mock('../api/libro', () => ({ useLibroQuery: () => ({ data: { operaciones: [] } }) }));
vi.mock('../api/conservacion', () => ({
  useConservacion: () => ({
    data: [
      {
        id: 'nueva',
        folio: 'Póliza nueva',
        numeroVersion: 2,
        reemplazaA: 'vieja',
        vigenteHasta: '2027-05-01',
      },
    ],
  }),
}));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
beforeEach(() => {
  state.mutate.mockReset();
  useAuthStore.setState({
    permisos: [
      P.ActivosFijosReversasAutorizar,
      P.ActivosFijosReversasSolicitar,
      P.ActivosFijosDocumentosGestionar,
    ],
    user: { id: 'daf', nombre: 'DAF', email: 'daf@example.invalid' },
  });
});
const op = {
  id: 'op',
  version: 4,
  tipo: 'ReversaBaja',
  registradoPor: 'contador',
  polizaId: null,
  anulada: false,
} as Operacion;
it('rechazar una reversa exige motivo y envía versión vigente', () => {
  render(<ResolverOperacion operacion={op} />);
  fireEvent.click(screen.getByRole('button', { name: 'Rechazar' }));
  expect(screen.getByRole('button', { name: 'Rechazar solicitud' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Motivo *'), { target: { value: 'No procede' } });
  fireEvent.click(screen.getByRole('button', { name: 'Rechazar solicitud' }));
  expect(state.mutate).toHaveBeenCalledWith(
    { ruta: 'operaciones/op/rechazar', version: 4, body: { motivo: 'No procede' } },
    expect.anything(),
  );
});
it('la misma persona no puede autorizar su reversa', () => {
  render(<ResolverOperacion operacion={{ ...op, registradoPor: 'daf' }} />);
  expect(screen.getByRole('button', { name: 'Autorizar' })).toBeDisabled();
});
it('sólo el capitalizador puede solicitar la reversa de capitalización', () => {
  const a = { id: 'a', estado: 1, polizaAltaId: 'p', capitalizadoPor: 'contador' } as Activo;
  const vista = render(<ReversaActivo activo={a} />);
  expect(screen.queryByRole('button', { name: 'Revertir capitalización' })).not.toBeInTheDocument();
  vista.rerender(<ReversaActivo activo={{ ...a, capitalizadoPor: 'daf' }} />);
  fireEvent.click(screen.getByRole('button', { name: 'Revertir capitalización' }));
  fireEvent.change(screen.getByLabelText('Fecha contable *'), { target: { value: '2026-11-05' } });
  fireEvent.change(screen.getByLabelText('Motivo *'), { target: { value: 'Corregir alta' } });
  fireEvent.click(screen.getByRole('button', { name: 'Solicitar autorización' }));
  expect(state.mutate).toHaveBeenCalledWith(
    expect.objectContaining({
      ruta: 'activos/a/operaciones',
      body: {
        tipo: 'ReversaCapitalizacion',
        fecha: '2026-11-05',
        importe: 0,
        motivo: 'Corregir alta',
        revierteA: null,
      },
    }),
    expect.anything(),
  );
});
it('renovación exige seleccionar una nueva versión con vigencia posterior', () => {
  render(
    <ResolverAviso
      aviso={{
        id: 'vieja',
        activoId: 'a',
        numero: 'AF1',
        descripcion: 'Equipo',
        referencia: 'Seguro',
        vence: '2026-05-01',
        esMantenimiento: false,
      }}
    />,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Resolver' }));
  fireEvent.change(screen.getByLabelText('Resolución *'), { target: { value: 'Renovado' } });
  const nuevo = screen.getByLabelText('Documento nuevo *');
  expect(nuevo).toBeRequired();
  fireEvent.change(nuevo, { target: { value: 'nueva' } });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar resolución' }));
  expect(state.mutate).toHaveBeenCalledWith(
    {
      ruta: 'activos/a/avisos/vieja/resolver',
      body: { resolucion: 'Renovado', motivo: null, soporte: null, documentoNuevoId: 'nueva' },
    },
    expect.anything(),
  );
});

it.each(['Baja', 'Venta'])(
  'quien registró %s puede retirarla con motivo sin permiso de autorizar',
  (tipo) => {
    useAuthStore.setState({ permisos: [P.ActivosFijosBajasRegistrar] });
    const vista = render(<ResolverOperacion operacion={{ ...op, tipo, registradoPor: 'daf' }} />);
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retirar' }));
    expect(screen.getByRole('button', { name: 'Retirar solicitud' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo *'), { target: { value: 'Error de captura' } });
    fireEvent.click(screen.getByRole('button', { name: 'Retirar solicitud' }));
    expect(state.mutate).toHaveBeenCalledWith(
      { ruta: 'operaciones/op/retirar', version: 4, body: { motivo: 'Error de captura' } },
      expect.anything(),
    );
    vista.rerender(
      <ResolverOperacion
        operacion={{ ...op, tipo, anulada: true, motivoRetiro: 'Error de captura' }}
      />,
    );
    expect(screen.getByText('Retirada: Error de captura')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retirar' })).not.toBeInTheDocument();
  },
);
it('otra persona no puede retirar la baja', () => {
  render(<ResolverOperacion operacion={{ ...op, tipo: 'Baja' }} />);
  expect(screen.queryByRole('button', { name: 'Retirar' })).not.toBeInTheDocument();
});
