import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MobileSidebar } from '@/components/layout/MobileSidebar';

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
  useLocation: () => ({ pathname: '/' }),
}));

describe('<MobileSidebar>', () => {
  it('open=false: no renderiza el dialog', () => {
    render(
      <MobileSidebar
        open={false}
        onOpenChange={() => {}}
        onModuloOpen={() => {}}
      />,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('open=true: renderiza con título sr-only y los items del nav', () => {
    render(
      <MobileSidebar
        open={true}
        onOpenChange={() => {}}
        onModuloOpen={() => {}}
      />,
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText(/Navegación principal/i)).toBeInTheDocument();
    // El nav incluye Inicio + 8 módulos.
    expect(screen.getByText('Inicio')).toBeInTheDocument();
    expect(screen.getByText('Compras')).toBeInTheDocument();
  });

  it('click en Inicio (link) cierra el drawer (onOpenChange(false))', () => {
    const onOpenChange = vi.fn();
    render(
      <MobileSidebar
        open={true}
        onOpenChange={onOpenChange}
        onModuloOpen={() => {}}
      />,
    );
    screen.getByText('Inicio').click();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('click en módulo cierra drawer Y dispara onModuloOpen', () => {
    const onOpenChange = vi.fn();
    const onModuloOpen = vi.fn();
    render(
      <MobileSidebar
        open={true}
        onOpenChange={onOpenChange}
        onModuloOpen={onModuloOpen}
      />,
    );
    // Compras es el único módulo NO disabled (cumple botón clickable).
    screen.getByRole('button', { name: /Compras/i }).click();
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onModuloOpen).toHaveBeenCalledOnce();
    expect(onModuloOpen.mock.calls[0][0]).toMatchObject({
      moduloId: 'compras',
    });
  });

  it('botón Cerrar del drawer dispara onOpenChange(false)', () => {
    const onOpenChange = vi.fn();
    render(
      <MobileSidebar
        open={true}
        onOpenChange={onOpenChange}
        onModuloOpen={() => {}}
      />,
    );
    screen.getByRole('button', { name: /cerrar navegación/i }).click();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
