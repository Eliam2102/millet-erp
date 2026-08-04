import { Link } from '@tanstack/react-router';
import { Card } from '@/components/ui/card';
import type { NavCard } from '@/lib/nav';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;AppLauncherCard/&gt;</c> — card individual del modal de App
 * Launcher (ADR-0032). Anatomía:
 *
 * <list>
 *   <item><b>Icono</b> (lucide) en cuadro tintado.</item>
 *   <item><b>Título</b> 1 línea.</item>
 *   <item><b>Descripción</b> 1-2 líneas (qué hace la pantalla).</item>
 *   <item><b>Footer slot</b> opcional para badges/contadores (ADR-0032
 *   R5: sin contadores reales en v1, pero el slot está listo).</item>
 * </list>
 *
 * <para>Toda la card es clickable — un <c>&lt;Link/&gt;</c> de
 * TanStack Router envuelve todo el contenido. <c>onSelect</c> se
 * invoca antes de navegar para que el modal contenedor se cierre
 * (ADR-0032: auto-close al elegir card).</para>
 *
 * <para>El gate de permiso se aplica a nivel <c>NavModulo</c> en
 * <c>filtrarModuloPorPermisos</c>; este componente asume que la card
 * que recibe ya pasó el gate.</para>
 */
export interface AppLauncherCardProps {
  card: NavCard;
  /** Callback antes de navegar — el modal lo usa para cerrarse. */
  onSelect: () => void;
  /** Footer slot para contadores/badges (UF7-PR3+). */
  footer?: React.ReactNode;
}

export function AppLauncherCard({
  card,
  onSelect,
  footer,
}: AppLauncherCardProps) {
  const Icon = card.icon;
  return (
    <Card
      className={cn(
        'transition-all hover:border-primary hover:shadow-md',
        'focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/30',
      )}
    >
      <Link
        to={card.to}
        onClick={onSelect}
        className="flex h-full flex-col gap-3 p-4 text-left"
      >
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
            <Icon className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="truncate text-sm font-semibold text-foreground">
              {card.label}
            </div>
            <div className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">
              {card.description}
            </div>
          </div>
        </div>
        {footer != null && (
          <div className="mt-auto flex items-center justify-end pt-1 text-xs text-muted-foreground">
            {footer}
          </div>
        )}
      </Link>
    </Card>
  );
}
