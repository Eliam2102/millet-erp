import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CentrosCostoLandingPage } from './CentrosCostoLandingPage';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * DoD de CECO-FE-PR1: "navegación completa por permisos" — las cards del
 * módulo aparecen/desaparecen según el permiso del rol (mismo
 * filtrarModuloPorPermisos del modal del sidebar).
 *
 * El Link del router se mockea (molde AppLauncherCard.test): aquí se
 * prueba el gating de visibilidad, no la navegación.
 */

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    onClick,
    className,
  }: {
    children: React.ReactNode;
    to: string;
    onClick?: () => void;
    className?: string;
  }) => (
    <a href={to} onClick={onClick} className={className}>
      {children}
    </a>
  ),
}));

function conPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<CentrosCostoLandingPage> — cards gateadas por permiso (DoD FE-PR1)', () => {
  it('con catalogo.leer solo se ve la card de configuración', () => {
    conPermisos(['centros_costo.catalogo.leer']);
    render(<CentrosCostoLandingPage />);

    expect(
      screen.getByRole('heading', { level: 1, name: /Centros de Costo/ }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Catálogo jerárquico de 3 niveles/)).toBeInTheDocument();
    expect(
      screen.queryByText('Asignación de Centros de Costo'),
    ).not.toBeInTheDocument();
  });

  it('con asignaciones.administrar solo se ve la card de asignación', () => {
    conPermisos(['centros_costo.asignaciones.administrar']);
    render(<CentrosCostoLandingPage />);

    expect(
      screen.getByText('Asignación de Centros de Costo'),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Catálogo jerárquico de 3 niveles/),
    ).not.toBeInTheDocument();
  });

  it('catalogo.administrar también habilita la card de configuración (any-of)', () => {
    conPermisos(['centros_costo.catalogo.administrar']);
    render(<CentrosCostoLandingPage />);

    expect(screen.getByText(/Catálogo jerárquico de 3 niveles/)).toBeInTheDocument();
  });

  it('sin permisos del módulo: mensaje neutro, cero cards', () => {
    conPermisos(['almacen.almacenes.leer']);
    render(<CentrosCostoLandingPage />);

    expect(screen.getByTestId('centros-costo-sin-cards')).toBeInTheDocument();
    expect(
      screen.queryByText(/Catálogo jerárquico de 3 niveles/),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText('Asignación de Centros de Costo'),
    ).not.toBeInTheDocument();
  });
});
