import '@testing-library/jest-dom/vitest';
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ListaUsuariosCompacta } from './ListaUsuariosCompacta';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, params }: { children: React.ReactNode; params: { id: string } }) => (
    <a href={`/admin/usuarios/${params.id}`}>{children}</a>
  ),
}));

describe('Cuentas de acceso', () => {
  it('distingue un usuario vinculado a empleado de una cuenta sin vínculo', () => {
    render(<ListaUsuariosCompacta idActivo={null} items={[
      { id: 'u-1', nombre: 'Ana', email: 'ana@millet.mx', entraOid: 'oid-1',
        departamentoId: null, activo: true, version: 1, empleadoId: 'e-1' },
      { id: 'u-2', nombre: 'Servicio', email: 'servicio@millet.mx', entraOid: 'oid-2',
        departamentoId: null, activo: true, version: 1, empleadoId: null },
    ]} />);

    expect(screen.getByText('Empleado vinculado')).toBeInTheDocument();
    expect(screen.getByText('Sin empleado vinculado')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Ana/ })).toHaveAttribute('href', '/admin/usuarios/u-1');
  });
});
