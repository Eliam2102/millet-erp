import { Fragment, type ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;Breadcrumbs/&gt;</c> — navegación jerárquica que se renderiza al
 * top de cada pantalla (doc 05 §13.9).
 *
 * <para>Cada item es <c>{ label, to?, search? }</c>. Items con <c>to</c>
 * son clicables (<c>&lt;Link/&gt;</c> de TanStack Router); el último
 * item (sin <c>to</c>) representa la página actual y se muestra como
 * texto plano.</para>
 *
 * <para><c>search</c> permite **preservar filtros** al navegar a un
 * ancestro. La pantalla de detalle declara el bread con
 * <c>{ to: '/compras/requisiciones', search: useSearch({ from: ... }) }</c>
 * y el click vuelve a la bandeja con los filtros que el usuario tenía.</para>
 *
 * @example
 * ```tsx
 * <Breadcrumbs items={[
 *   { label: 'Compras', to: '/compras' },
 *   { label: 'Requisiciones', to: '/compras/requisiciones', search: filtros },
 *   { label: rq.folio },
 * ]} />
 * ```
 */
export interface BreadcrumbItem {
  /** Texto visible. */
  label: ReactNode;
  /**
   * Ruta destino. Cuando se omite, el item se renderiza como texto plano
   * (representa la página actual).
   */
  to?: string;
  /**
   * Search params a preservar al navegar al ancestro. Útil para
   * restaurar filtros de bandeja al volver desde el detalle.
   */
  search?: Record<string, unknown>;
}

export interface BreadcrumbsProps {
  items: BreadcrumbItem[];
  className?: string;
}

export function Breadcrumbs({ items, className }: BreadcrumbsProps) {
  if (items.length === 0) return null;

  return (
    <nav
      aria-label="breadcrumb"
      className={cn(
        'mb-4 flex items-center gap-1.5 text-sm text-muted-foreground',
        className,
      )}
    >
      <ol className="flex flex-wrap items-center gap-1.5">
        {items.map((item, idx) => {
          const esUltimo = idx === items.length - 1;
          return (
            <Fragment key={`${idx}-${typeof item.label === 'string' ? item.label : ''}`}>
              <li className="inline-flex items-center">
                {item.to != null && !esUltimo ? (
                  <Link
                    to={item.to}
                    search={item.search}
                    className="transition-colors hover:text-foreground"
                  >
                    {item.label}
                  </Link>
                ) : (
                  <span
                    aria-current={esUltimo ? 'page' : undefined}
                    className={cn(
                      esUltimo && 'font-medium text-foreground',
                    )}
                  >
                    {item.label}
                  </span>
                )}
              </li>
              {!esUltimo && (
                <li aria-hidden="true" className="inline-flex items-center">
                  <ChevronRight className="h-4 w-4 opacity-60" />
                </li>
              )}
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}
