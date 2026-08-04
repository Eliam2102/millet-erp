import { Link } from '@tanstack/react-router';
import { Card } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * <c>&lt;AdminLandingCard/&gt;</c> — card individual del landing
 * <c>/admin</c>. Reusa el patrón visual de
 * <c>&lt;AppLauncherCard/&gt;</c> (ADR-0032) para que el área de
 * Administración se sienta consistente con el shell de navegación: la
 * card entera es clickable y linkea a <see cref="AdminSection.href"/>.
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
    <Card
      className={cn(
        'transition-all hover:border-primary hover:shadow-md',
        'focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/30',
      )}
    >
      <Link
        to={section.href}
        className="flex h-full flex-col gap-3 p-4 text-left"
      >
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
            <Icon className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="truncate text-sm font-semibold text-foreground">
              {section.titulo}
            </div>
            <div className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">
              {section.descripcion}
            </div>
          </div>
        </div>
      </Link>
    </Card>
  );
}
