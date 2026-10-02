import { fireEvent, render, screen } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { ModulePanel } from './ModulePanel';
import { contextoNavegacion } from '@/lib/nav';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', () => ({
  useLocation: () => ({ pathname: '/compras/ordenes/oc-123' }),
  Link: ({ to, children, ...props }: { to: string; children: React.ReactNode }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));

it('filtra sin acentos, conserva selección y opciones permitidas, y permite contraer', () => {
  const permisos = [
    PermisosCanonicos.ComprasOrdenesLeer,
    PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
  ];
  const modulo = contextoNavegacion('/compras/ordenes/oc-123', permisos)!.modulo;
  const onCollapse = vi.fn();
  render(<ModulePanel modulo={modulo} onCollapse={onCollapse} />);
  expect(screen.getByRole('link', { name: 'Órdenes de compra' })).toHaveAttribute(
    'aria-current',
    'page',
  );
  expect(screen.queryByRole('link', { name: 'Aprobadores' })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Filtrar menú del módulo'), {
    target: { value: 'autorizacion' },
  });
  expect(screen.getByRole('link', { name: 'OCs pendientes de autorización' })).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Órdenes de compra' })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Filtrar menú del módulo'), {
    target: { value: 'inexistente' },
  });
  expect(screen.getByRole('status')).toHaveTextContent('No hay opciones');
  fireEvent.click(screen.getByRole('button', { name: 'Contraer panel' }));
  expect(onCollapse).toHaveBeenCalledOnce();
});
