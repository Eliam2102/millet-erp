import type { ReactNode } from 'react';
import { TriangleAlert } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface AvisoEnLineaProps {
  /** `advertencia` = nota (`role="note"`); `error` = bloqueo (`role="alert"`). */
  tono: 'advertencia' | 'error';
  titulo?: string;
  /** Qué falta y cómo se destraba. */
  children: ReactNode;
  /** Acción opcional (botón o enlace). */
  accion?: ReactNode;
}

/**
 * Callout persistente en línea (DS 4.10). Úsalo para errores de negocio que
 * no pertenecen a un solo campo o para avisos que deben seguir visibles.
 */
export function AvisoEnLinea({ tono, titulo, children, accion }: AvisoEnLineaProps) {
  const esError = tono === 'error';
  return (
    <div
      role={esError ? 'alert' : 'note'}
      className={cn(
        'flex items-start gap-2 rounded-md px-3 py-2.5 text-sm',
        esError ? 'bg-danger-bg text-danger-fg' : 'bg-warning-note-bg text-warning-note-fg',
      )}
    >
      <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <div className="min-w-0 flex-1">
        {titulo && <p className="font-medium">{titulo}</p>}
        <div>{children}</div>
      </div>
      {accion && <div className="shrink-0">{accion}</div>}
    </div>
  );
}
