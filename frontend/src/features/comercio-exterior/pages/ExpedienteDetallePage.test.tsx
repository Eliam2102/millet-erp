import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { muestra } from '../../../../tools/ce-expedientes-layout/muestra';
import { ExpedienteDetallePage } from './ExpedienteDetallePage';
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: React.PropsWithChildren<{ to: string }>) => <a href={to}>{children}</a>,
}));
vi.mock('../api/hooks', () => ({
  useExpediente: () => ({ data: { data: muestra } }),
  useExpedientes: () => ({ data: { items: [muestra.expediente] } }),
  useAccionExpediente: () => ({ mutateAsync: vi.fn() }),
}));
vi.mock('../components/CierresPanel', () => ({
  CierresPanel: () => null,
  FacturasPanel: () => null,
}));
vi.mock('@/components/erp/selectors/IncotermSelector', () => ({ IncotermSelector: () => null }));
vi.mock('@/components/erp/selectors/MonedaSelector', () => ({ MonedaSelector: () => null }));
beforeEach(() =>
  useAuthStore.setState({
    permisos: [P.ComercioExteriorExpedientesLeer, P.ComercioExteriorExpedientesEditar],
  }),
);
it('pliega la lista por control accesible y conserva lo capturado', () => {
  render(<ExpedienteDetallePage id="ce-muestra" />, { wrapper: createQueryWrapper() });
  expect(
    screen.queryByRole('complementary', { name: 'Lista de expedientes' }),
  ).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Editar datos' }));
  const referencia = screen.getByLabelText(/Referencia/);
  fireEvent.change(referencia, { target: { value: 'Captura en curso' } });
  fireEvent.click(screen.getByRole('button', { name: 'Mostrar expedientes' }));
  expect(screen.getByRole('button', { name: 'Ocultar expedientes' })).toHaveAttribute(
    'aria-expanded',
    'true',
  );
  expect(screen.getByRole('complementary', { name: 'Lista de expedientes' })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Ocultar expedientes' }));
  expect(referencia).toHaveValue('Captura en curso');
  expect(screen.getAllByRole('tab')).toHaveLength(10);
});
it('conserva consulta de sólo lectura sin exponer edición', () => {
  useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesLeer] });
  render(<ExpedienteDetallePage id="ce-muestra" />, { wrapper: createQueryWrapper() });
  expect(screen.queryByRole('button', { name: 'Editar datos' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Cancelar expediente' })).not.toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'Seguimiento del expediente' })).toHaveTextContent(
    'Listo para cierre',
  );
});
