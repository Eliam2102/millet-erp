import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Inbox } from 'lucide-react';
import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import type { NavCard } from '@/lib/nav';

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

const cardEjemplo: NavCard = {
  label: 'Mis requisiciones',
  description: 'Bandeja general de requisiciones — crear, ver, editar.',
  to: '/compras/requisiciones',
  icon: Inbox,
};

describe('<AppLauncherCard>', () => {
  it('renderiza título + descripción + link al destino', () => {
    render(<AppLauncherCard card={cardEjemplo} onSelect={() => {}} />);
    expect(screen.getByText('Mis requisiciones')).toBeInTheDocument();
    expect(
      screen.getByText(/bandeja general/i),
    ).toBeInTheDocument();
    const link = screen.getByRole('link');
    expect(link).toHaveAttribute('href', '/compras/requisiciones');
  });

  it('click invoca onSelect (para que el modal se cierre)', () => {
    const onSelect = vi.fn();
    render(<AppLauncherCard card={cardEjemplo} onSelect={onSelect} />);
    screen.getByRole('link').click();
    expect(onSelect).toHaveBeenCalledOnce();
  });

  it('footer slot opcional: no se renderiza cuando footer=undefined', () => {
    const { container } = render(
      <AppLauncherCard card={cardEjemplo} onSelect={() => {}} />,
    );
    // El componente solo renderiza el footer block cuando footer != null.
    expect(container.querySelector('[class*="mt-auto"]')).toBeNull();
  });

  it('footer slot: renderiza contenido cuando footer se pasa', () => {
    render(
      <AppLauncherCard
        card={cardEjemplo}
        onSelect={() => {}}
        footer={<span>12 pendientes</span>}
      />,
    );
    expect(screen.getByText('12 pendientes')).toBeInTheDocument();
  });
});
