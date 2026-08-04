import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;EmptyState/&gt;</c> — patrón estándar para mostrar el "sin datos"
 * de cualquier listado, bandeja o detalle. Doc 05 §13.1: cada pantalla
 * con datos asíncronos debe diseñar los 3 estados (loading / empty /
 * error) explícitamente; un PR sin los 3 no se mergea.
 *
 * <para>API mínima y agnóstica de módulo. El icono se pasa como
 * <c>ReactNode</c> para no acoplarse a una librería específica
 * (típicamente <c>lucide-react</c>, pero un emoji o un SVG inline
 * también valen).</para>
 *
 * @example
 * ```tsx
 * import { Inbox } from 'lucide-react';
 * <EmptyState
 *   icon={<Inbox className="h-10 w-10" />}
 *   title="Aún no tienes requisiciones."
 *   description="Crea la primera para empezar."
 *   action={<Button onClick={() => useNuevaRequisicion().abrir()}>Nueva RQ</Button>}
 * />
 * ```
 */
export interface EmptyStateProps {
  /** Icono visual. Tamaño esperado h-10 w-10 (40px). Opcional. */
  icon?: ReactNode;
  /** Título corto, una sola línea, sentence-case. */
  title: string;
  /** Descripción opcional, hasta 2 líneas. Resuelve "¿qué hago ahora?". */
  description?: string;
  /** CTA principal. Suele ser un <c>&lt;Button/&gt;</c> envolviendo un Link. */
  action?: ReactNode;
  /** Override de clases del wrapper exterior. */
  className?: string;
}

export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
}: EmptyStateProps) {
  return (
    <div
      role="status"
      className={cn(
        'flex flex-col items-center justify-center gap-3 px-6 py-12 text-center',
        className,
      )}
    >
      {icon != null && (
        <div className="text-muted-foreground" aria-hidden="true">
          {icon}
        </div>
      )}
      <h3 className="text-base font-semibold">{title}</h3>
      {description != null && (
        <p className="max-w-md text-sm text-muted-foreground">{description}</p>
      )}
      {action != null && <div className="mt-2">{action}</div>}
    </div>
  );
}
