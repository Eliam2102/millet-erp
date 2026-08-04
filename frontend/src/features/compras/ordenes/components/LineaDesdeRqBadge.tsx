import { LinkIcon } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;LineaDesdeRqBadge/&gt;</c> — chip pequeño que indica que una
 * línea de OC fue heredada de una requisición (mode 1:1 o
 * consolidación N:1). Mostrado en la tabla del editor de líneas
 * (UF2-PR3-b) cuando <c>linea.requisicionId != null</c>.
 *
 * <para>Visual: pill azul con icono de link + folio (si lo tenemos).
 * Si solo tenemos el id de la RQ y no su folio (no fetcheamos
 * separadamente para evitar N+1), mostramos un short-id del UUID
 * como hint mientras la UI de UF7-PR3 (árbol documentos) entrega
 * el folio enriquecido.</para>
 *
 * <para>Click podría navegar al detalle de la RQ — diferido a
 * UF7-PR3 que cubre cross-doc navigation. Por ahora el badge es
 * read-only.</para>
 */
export interface LineaDesdeRqBadgeProps {
  /** Id de la RQ origen. */
  requisicionId: string;
  /** Folio de la RQ si está disponible (caller lo resuelve si tiene
   * cache; si no, se muestra short-id). */
  folio?: string | null;
  className?: string;
}

export function LineaDesdeRqBadge({
  requisicionId,
  folio,
  className,
}: LineaDesdeRqBadgeProps) {
  const label = folio ? `RQ ${folio}` : `RQ-${requisicionId.slice(0, 8)}`;
  const tooltip = folio
    ? `Línea heredada de RQ ${folio} (id ${requisicionId})`
    : `Línea heredada de la requisición ${requisicionId}`;

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full border border-blue-200 bg-blue-50 px-1.5 py-0.5 text-[0.65rem] font-medium text-blue-900',
        className,
      )}
      title={tooltip}
      data-component="linea-desde-rq-badge"
      data-requisicion-id={requisicionId}
    >
      <LinkIcon className="h-2.5 w-2.5" aria-hidden="true" />
      Desde {label}
    </span>
  );
}
