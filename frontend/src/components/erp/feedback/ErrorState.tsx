import { useState } from 'react';
import { TriangleAlert } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { codigoSoporte, type ProblemDetails } from '@/lib/api/error';
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

function mensajeUsuario(mensaje?: string) {
  const texto = mensaje?.trim();
  return texto === 'Ocurrió un error inesperado. Reporta el código de seguimiento traceId a soporte.'
    ? 'Ocurrió un error inesperado. Reporta el código de seguimiento a soporte.'
    : texto || undefined;
}

export function ErrorState({ problem, title, onRetry, className }: ErrorStateProps) {
  const tituloServidor = mensajeUsuario(problem?.title);
  const tituloFinal = title?.trim() || tituloServidor || 'Algo salió mal';
  const noEncontrado = problem?.status === 404;
  const detalleServidor = mensajeUsuario(problem?.detail);
  const causa = (detalleServidor !== tituloFinal ? detalleServidor : undefined)
    || (tituloServidor !== tituloFinal ? tituloServidor : undefined);
  const detalle = noEncontrado
    ? 'El registro no existe o ya no está disponible.'
    : causa || (detalleServidor ? undefined : 'No se pudo completar la solicitud. Intenta de nuevo; si persiste, avisa a soporte.');
  const codigo = codigoSoporte(problem);
  const traceId = problem?.traceId;
  const [copia, setCopia] = useState<{ traceId: string; mensaje: string } | null>(null);

  async function copiarCodigo() {
    if (!traceId) return;
    try {
      await navigator.clipboard.writeText(traceId);
      setCopia({ traceId, mensaje: 'Código copiado.' });
    } catch {
      setCopia({ traceId, mensaje: 'No se pudo copiar. Selecciona el código y cópialo.' });
    }
  }

  return (
    <div className={cn('flex flex-col items-center gap-4 px-6 py-12', className)}>
      <Alert variant="destructive" className="max-w-xl text-left">
        <TriangleAlert className="h-4 w-4" />
        <AlertTitle>{tituloFinal}</AlertTitle>
        <AlertDescription>
          {detalle}
          {codigo != null && (
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <span className="select-text break-all font-mono text-xs">{codigo}</span>
              <Button type="button" variant="outline" size="sm" onClick={copiarCodigo}>
                Copiar código
              </Button>
              <span role="status" className="text-xs">
                {copia?.traceId === traceId ? copia?.mensaje : null}
              </span>
            </div>
          )}
        </AlertDescription>
      </Alert>
      {onRetry != null && !noEncontrado && (
        <Button variant="outline" onClick={onRetry}>
          Reintentar
        </Button>
      )}
    </div>
  );
}
