import { TriangleAlert } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { type ProblemDetails } from '@/lib/api/error';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ErrorState/&gt;</c> — patrón estándar para mostrar un fallo de
 * carga (5xx, network, etc.). Doc 05 §13.1.
 *
 * <para>Toma el <c>ProblemDetails</c> del <see cref="ApiError"/> y muestra
 * <c>title</c> + <c>detail</c> + <c>traceId</c> (visible para que el
 * usuario lo dicte a soporte). Si no se pasa <c>problem</c>, se cae a un
 * mensaje genérico.</para>
 *
 * <para>Para 4xx con error estructurado por campo (validación), usar
 * <c>applyServerErrors</c> + toast — este componente cubre fallos
 * "página entera no se pudo cargar".</para>
 *
 * @example
 * ```tsx
 * <ErrorState problem={query.error?.problem} onRetry={() => query.refetch()} />
 * ```
 */
export interface ErrorStateProps {
  /** Payload <c>application/problem+json</c> del backend. Opcional. */
  problem?: ProblemDetails;
  /** Override del título. Si no, usa <c>problem.title</c> o un default. */
  title?: string;
  /** Handler del botón "Reintentar". Si no se pasa, el botón no se muestra. */
  onRetry?: () => void;
  /** Override de clases del wrapper exterior. */
  className?: string;
}

export function ErrorState({
  problem,
  title,
  onRetry,
  className,
}: ErrorStateProps) {
  const tituloFinal = title ?? problem?.title ?? 'Algo salió mal';
  const detalle = problem?.detail;
  const traceId = problem?.traceId;

  return (
    <div
      role="alert"
      className={cn('flex flex-col items-center gap-4 px-6 py-12', className)}
    >
      <Alert variant="destructive" className="max-w-xl text-left">
        <TriangleAlert className="h-4 w-4" />
        <AlertTitle>{tituloFinal}</AlertTitle>
        <AlertDescription>
          {detalle ?? 'Si el problema persiste, reporta el código a soporte.'}
          {traceId != null && (
            <span className="mt-2 block font-mono text-xs opacity-80">
              Código: {traceId}
            </span>
          )}
        </AlertDescription>
      </Alert>
      {onRetry != null && (
        <Button variant="outline" onClick={onRetry}>
          Reintentar
        </Button>
      )}
    </div>
  );
}
