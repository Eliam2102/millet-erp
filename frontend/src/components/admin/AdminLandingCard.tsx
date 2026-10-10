import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * <c>&lt;AdminLandingCard/&gt;</c> — card individual del landing
 * <c>/admin</c>. Reusa el patrón visual de
 * <c>&lt;AppLauncherCard/&gt;</c> (ADR-0032) para que el área de
 * Administración se sienta consistente con el shell de navegación: la
 * fila entera es clickable y linkea a <see cref="AdminSection.href"/>.
 *
 * <para>El gate por <c>permisoRequerido</c> se aplica en
 * <see cref="useAdminRegistry"/>; esta card asume que recibió un item
 * ya filtrado.</para>
 */
export interface AdminLandingCardProps {
  section: AdminSection;
}

export function AdminLandingCard({ section }: AdminLandingCardProps) {
  const Icon = section.icon;
  return (
    <Link
      to={section.href}
      className={cn(
        'grid grid-cols-[32px_1fr_12px] items-center gap-2.5 rounded-lg bg-surface-card px-3 py-2.5 shadow-card-flat transition-shadow',
        'hover:ring-1 hover:ring-brand focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand',
      )}
    >
      <span className="flex h-8 w-8 items-center justify-center rounded-md bg-surface-selected text-brand">
        <Icon size={18} strokeWidth={1.6} aria-hidden="true" />
      </span>
      <span className="min-w-0">
        <span className="block truncate text-sm font-medium text-ink">{section.titulo}</span>
        <span className="block truncate text-xs text-ink-muted">{section.descripcion}</span>
      </span>
      <ChevronRight size={16} strokeWidth={1.6} aria-hidden="true" className="text-ink-subtle" />
    </Link>
  );
}
