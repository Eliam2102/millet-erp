import { AlertTriangle } from 'lucide-react';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { ErrorState } from '@/components/erp';
import { useCreditoDisponible } from '@/features/cxc/api/useLineasCredito';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { esApiError } from '@/lib/api';
import { ChipEstadoLinea } from '@/features/cxc/components/ChipEstadoLinea';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CreditoDisponibleCard/&gt;</c> — crédito disponible del cliente
 * por línea/moneda (CXC-PR2/PR3): <c>disponible = límite − facturado −
 * liberado sin factura</c>. Nunca convierte divisas: un renglón por
 * moneda.
 *
 * <para>Mientras el gap G1 con A+W siga abierto, <c>datoIncompleto</c>
 * viene en <c>true</c> y el número se muestra con badge ámbar "sin
 * material liberado A+W" (05-frontend-diseno §4.2). El dato NO se
 * oculta: se enseña qué le falta.</para>
 */
export function CreditoDisponibleCard({
  clienteId,
  className,
}: {
  clienteId: string;
  className?: string;
}) {
  const query = useCreditoDisponible(clienteId);

  return (
    <section
      className={cn('rounded-md border bg-card p-4', className)}
      aria-label="Crédito disponible"
    >
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-medium">Crédito disponible</h3>
        {query.data?.datoIncompleto && <BadgeDatoIncompleto />}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo evaluar el crédito disponible"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="mt-3 space-y-2">
          <div className="h-8 w-48 animate-pulse rounded bg-muted" />
          <div className="h-4 w-64 animate-pulse rounded bg-muted" />
        </div>
      ) : query.data.lineas.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">
          El cliente no tiene líneas de crédito.
        </p>
      ) : (
        <ul className="mt-3 space-y-3">
          {query.data.lineas.map((l) => (
            <li key={l.lineaCreditoId} className="space-y-1">
              <div className="flex items-baseline justify-between gap-2">
                <span
                  className={cn(
                    'font-mono text-2xl font-semibold tabular-nums',
                    l.disponible < 0 && 'text-destructive',
                  )}
                >
                  {formatoMonto(l.disponible, l.moneda)}
                </span>
                <ChipEstadoLinea estado={l.estado} />
              </div>
              <p className="text-xs text-muted-foreground">
                Límite {formatoMonto(l.limite, l.moneda)} − facturado{' '}
                {formatoMonto(l.facturado, l.moneda)} − liberado sin factura{' '}
                {formatoMonto(l.liberadoSinFactura, l.moneda)} · plazo{' '}
                {l.plazoDias} días
              </p>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/**
 * Badge ámbar del término faltante de la fórmula (gap G1). Tooltip
 * explica qué significa — el número es utilizable pero optimista.
 */
function BadgeDatoIncompleto() {
  return (
    <TooltipProvider delayDuration={150}>
      <Tooltip>
        <TooltipTrigger asChild>
        <span
          className="inline-flex cursor-help items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-medium text-amber-800 dark:bg-amber-950 dark:text-amber-300"
          data-testid="badge-dato-incompleto"
        >
          <AlertTriangle className="h-3 w-3" aria-hidden="true" />
          sin material liberado A+W
        </span>
        </TooltipTrigger>
        <TooltipContent className="max-w-72">
          El término &quot;liberado sin factura&quot; (material liberado en
          A+W aún no facturado) todavía no se integra a la fórmula — el
          disponible mostrado puede ser mayor al real. Pendiente de
          definición con el equipo A+W (gap G1).
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
