import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { Inbox, ShoppingCart } from 'lucide-react';
import { AppLauncherModal } from '@/components/layout/AppLauncherModal';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { NavModulo } from '@/lib/nav';

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

function setupSession(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

const moduloCompras: NavModulo = {
  moduloId: 'compras',
  label: 'Compras',
  icon: ShoppingCart,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Mis requisiciones',
          description: 'Bandeja general',
          to: '/compras/requisiciones',
          icon: Inbox,
          permission: 'compras.requisiciones.leer',
        },
        {
          label: 'Pendientes de autorización',
          description: 'En espera de N1/N2',
          to: '/compras/pendientes',
          icon: Inbox,
          permissionsAny: [
            'compras.requisiciones.autorizar-nivel1',
            'compras.requisiciones.autorizar-nivel2',
          ],
        },
      ],
    },
  ],
};

const moduloMultiSeccion: NavModulo = {
  moduloId: 'cxp',
  label: 'Cuentas por Pagar',
  icon: ShoppingCart,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Facturas',
          description: 'Bandeja general de facturas',
          to: '/cxp/facturas',
          icon: Inbox,
          permission: 'cxp.facturas.leer',
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Antigüedad de saldos',
          description: 'Cartera viva por buckets',
          to: '/cxp/reportes/antiguedad',
          icon: Inbox,
          permission: 'cxp.reportes.antiguedad',
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Aprobadores',
          description: 'Catálogo de aprobadores',
          to: '/cxp/admin/aprobadores',
          icon: Inbox,
          permission: 'cxp.catalogos.aprobadores',
        },
      ],
    },
  ],
};

beforeEach(() => setupSession([]));
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

describe('<AppLauncherModal>', () => {
  it('open=false: no renderiza nada', () => {
    setupSession(['compras.requisiciones.leer']);
    render(
      <AppLauncherModal
        modulo={moduloCompras}
        open={false}
        onOpenChange={() => {}}
      />,
    );
    expect(screen.queryByText('Compras')).not.toBeInTheDocument();
  });

  it('modulo=null: no renderiza modal', () => {
    setupSession(['compras.requisiciones.leer']);
    const { container } = render(
      <AppLauncherModal modulo={null} open={true} onOpenChange={() => {}} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('con permisos completos: renderiza ambas cards bajo "Operación"', () => {
    setupSession([
      'compras.requisiciones.leer',
      'compras.requisiciones.autorizar-nivel1',
    ]);
    render(
      <AppLauncherModal
        modulo={moduloCompras}
        open={true}
        onOpenChange={() => {}}
      />,
    );
    expect(screen.getAllByText('Compras').length).toBeGreaterThan(0);
    expect(screen.getByText('Operación')).toBeInTheDocument();
    expect(screen.getByText('Mis requisiciones')).toBeInTheDocument();
    expect(screen.getByText('Pendientes de autorización')).toBeInTheDocument();
  });

  it('solo permiso leer: oculta card de Pendientes', () => {
    setupSession(['compras.requisiciones.leer']);
    render(
      <AppLauncherModal
        modulo={moduloCompras}
        open={true}
        onOpenChange={() => {}}
      />,
    );
    expect(screen.getByText('Mis requisiciones')).toBeInTheDocument();
    expect(
      screen.queryByText('Pendientes de autorización'),
    ).not.toBeInTheDocument();
  });

  it('sin ningún permiso: muestra mensaje neutro (R3)', () => {
    setupSession([]);
    render(
      <AppLauncherModal
        modulo={moduloCompras}
        open={true}
        onOpenChange={() => {}}
      />,
    );
    expect(
      screen.getByText(/sin pantallas disponibles para tu rol/i),
    ).toBeInTheDocument();
    expect(screen.queryByText('Operación')).not.toBeInTheDocument();
  });

  it('una sola sección: sin tabs, con heading de sección', () => {
    setupSession(['compras.requisiciones.leer']);
    render(
      <AppLauncherModal
        modulo={moduloCompras}
        open={true}
        onOpenChange={() => {}}
      />,
    );
    expect(screen.queryAllByRole('tab')).toHaveLength(0);
    expect(screen.getByText('Operación')).toBeInTheDocument();
  });

  it('multi-sección: tabs por sección y solo la primera visible (R8)', () => {
    setupSession([
      'cxp.facturas.leer',
      'cxp.reportes.antiguedad',
      'cxp.catalogos.aprobadores',
    ]);
    render(
      <AppLauncherModal
        modulo={moduloMultiSeccion}
        open={true}
        onOpenChange={() => {}}
      />,
    );
    // Dos tablists (vertical md+ y pills mobile) → 2 tabs por sección.
    expect(screen.getAllByRole('tab', { name: 'Operación' })).toHaveLength(2);
    expect(screen.getAllByRole('tab', { name: 'Reportes' })).toHaveLength(2);
    expect(
      screen.getAllByRole('tab', { name: 'Configuración' }),
    ).toHaveLength(2);
    expect(screen.getByText('Facturas')).toBeInTheDocument();
    expect(
      screen.queryByText('Antigüedad de saldos'),
    ).not.toBeInTheDocument();
    expect(screen.queryByText('Aprobadores')).not.toBeInTheDocument();
  });

  it('multi-sección: click en tab cambia el panel activo', () => {
    setupSession([
      'cxp.facturas.leer',
      'cxp.reportes.antiguedad',
      'cxp.catalogos.aprobadores',
    ]);
    render(
      <AppLauncherModal
        modulo={moduloMultiSeccion}
        open={true}
        onOpenChange={() => {}}
      />,
    );
    fireEvent.click(screen.getAllByRole('tab', { name: 'Reportes' })[0]);
    expect(screen.getByText('Antigüedad de saldos')).toBeInTheDocument();
    expect(screen.queryByText('Facturas')).not.toBeInTheDocument();
    expect(
      screen
        .getAllByRole('tab', { name: 'Reportes' })
        .every((t) => t.getAttribute('aria-selected') === 'true'),
    ).toBe(true);
  });

  it('multi-sección con permisos parciales: sección filtrada no genera tab', () => {
    setupSession(['cxp.facturas.leer', 'cxp.reportes.antiguedad']);
    render(
      <AppLauncherModal
        modulo={moduloMultiSeccion}
        open={true}
        onOpenChange={() => {}}
      />,
    );
    expect(
      screen.queryAllByRole('tab', { name: 'Configuración' }),
    ).toHaveLength(0);
    expect(screen.getAllByRole('tab', { name: 'Operación' })).toHaveLength(2);
  });

  it('click en card: invoca onOpenChange(false) para auto-cerrar', () => {
    setupSession(['compras.requisiciones.leer']);
    const onOpenChange = vi.fn();
    render(
      <AppLauncherModal
        modulo={moduloCompras}
        open={true}
        onOpenChange={onOpenChange}
      />,
    );
    screen.getByText('Mis requisiciones').click();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
