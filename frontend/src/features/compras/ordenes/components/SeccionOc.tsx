import { useId, type ReactNode } from 'react';
import { Check } from 'lucide-react';
import { cn } from '@/lib/utils';

export interface EstadoSeccionOc {
  /** Errores de validación vigentes en la sección. */
  errores: number;
  /** Campos obligatorios completos y sin errores. */
  completa: boolean;
}

/** Indicador: número (falta algo), ✓ verde (completa) o ! rojo (errores). */
function IndicadorSeccion({ numero, estado }: { numero: number; estado: EstadoSeccionOc }) {
  const { errores, completa } = estado;
  const etiqueta =
    errores > 0
      ? `${errores} ${errores === 1 ? 'dato pendiente' : 'datos pendientes'}`
      : completa
        ? 'Sección completa'
        : `Sección ${numero} por completar`;
  return (
    <span
      role="img"
      aria-label={etiqueta}
      data-estado={errores > 0 ? 'error' : completa ? 'completa' : 'pendiente'}
      className={cn(
        'inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold',
        errores > 0
          ? 'bg-danger-bg text-danger-fg'
          : completa
            ? 'bg-success-bg text-success-fg'
            : 'bg-neutral-bg text-neutral-fg',
      )}
    >
      {errores > 0 ? '!' : completa ? <Check className="h-3.5 w-3.5" aria-hidden="true" /> : numero}
    </span>
  );
}

/** Sección numerada del formulario de OC con indicador de estado. */
export function SeccionOc({
  numero,
  titulo,
  subtitulo,
  estado,
  derecha,
  children,
}: {
  numero: number;
  titulo: string;
  subtitulo?: string;
  estado: EstadoSeccionOc;
  /** Contenido a la derecha del título (insignia). Los errores lo reemplazan. */
  derecha?: ReactNode;
  children: ReactNode;
}) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="space-y-3.5" data-seccion={numero}>
      <div>
        <div className="flex items-center gap-2.5">
          <IndicadorSeccion numero={numero} estado={estado} />
          <h3 id={id} className="text-sm font-semibold text-ink">
            {titulo}
          </h3>
          {estado.errores > 0 ? (
            <span className="ml-auto text-xs text-danger-fg" aria-hidden="true">
              {estado.errores} {estado.errores === 1 ? 'dato pendiente' : 'datos pendientes'}
            </span>
          ) : (
            derecha != null && <span className="ml-auto">{derecha}</span>
          )}
        </div>
        {subtitulo && <p className="mt-1 pl-[34px] text-xs text-ink-muted">{subtitulo}</p>}
      </div>
      {children}
    </section>
  );
}
