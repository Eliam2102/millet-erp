import type { ReactNode } from 'react';
import { TriangleAlert } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ContextoIncidencia } from '@/features/incidencias/api/types';
import { RegistrarIncidenciaBoton } from '@/features/incidencias/components/RegistrarIncidencia';

export interface AvisoEnLineaProps {
  /** `advertencia` = nota (`role="note"`); `error` = bloqueo (`role="alert"`). */
  tono: 'advertencia' | 'error';
  titulo?: string;
  /** Qué falta y cómo se destraba. */
  children: ReactNode;
  /** Acción opcional (botón o enlace). */
  accion?: ReactNode;
  /**
   * En un error de bloqueo, agrega «Registrar incidencia» prellenada con el código y el motivo del
   * rechazo (`error`) y el documento y paso donde ocurrió (`contexto`). Requiere el permiso
   * `compartido.incidencias.registrar`; sin él el botón no aparece.
   */
  incidencia?: { contexto: ContextoIncidencia; error?: unknown };
}

/**
 * Callout persistente en línea (DS 4.10). Úsalo para errores de negocio que
 * no pertenecen a un solo campo o para avisos que deben seguir visibles.
 */
export function AvisoEnLinea({ tono, titulo, children, accion, incidencia }: AvisoEnLineaProps) {
  const esError = tono === 'error';
  const registrar =
    esError && incidencia ? <RegistrarIncidenciaBoton contexto={incidencia.contexto} error={incidencia.error} /> : null;
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
      {(accion || registrar) && (
        <div className="flex shrink-0 flex-wrap items-center gap-2">
          {accion}
          {registrar}
        </div>
      )}
    </div>
  );
}
