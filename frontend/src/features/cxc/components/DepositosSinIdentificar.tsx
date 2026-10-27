import { Landmark } from 'lucide-react';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import type { DepositoPorIdentificarItem } from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { DIAS_ESPERA_ALERTA, diasEnEspera } from '@/features/cxc/lib/dias-en-espera';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

export interface DepositosSinIdentificarProps {
  /** Lista de CxC o de Tesorería (mismo contrato JSON). */
  query: {
    data: DepositoPorIdentificarItem[] | undefined;
    isLoading: boolean;
    isError: boolean;
    error?: unknown;
    refetch?: () => void;
  };
  /** Búsqueda de la bandeja (referencia, cuenta o cliente). */
  q: string;
  /** Sin callback no se muestra «Identificar» (requiere aplicacion-pago.proponer). */
  onIdentificar?: (deposito: DepositoPorIdentificarItem) => void;
}

/**
 * C7.4 · CA7.9 (regla R13): depósitos de cliente que llegaron sin detalle
 * (remittance). Quedan «sin identificar» y NO se aplican hasta que el cliente
 * manda el detalle; «Identificar» abre la nueva propuesta prellenada y
 * Tesorería confirma (quien propone no confirma, R12). Incluye el sobrante de
 * un depósito ya confirmado parcialmente.
 */
export function DepositosSinIdentificar({ query, q, onIdentificar }: DepositosSinIdentificarProps) {
  if (query.isError) {
    return (
      <ErrorState
        title="No se pudieron cargar los depósitos sin identificar"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch?.()}
      />
    );
  }
  if (query.isLoading) {
    return (
      <TableSkeleton
        rows={4}
        columns={[{ width: 'w-28' }, { width: 'w-40' }, { width: 'w-32' }, { width: 'w-28' }, { width: 'w-20' }]}
      />
    );
  }

  const items = (query.data ?? []).filter((d) => {
    if (q.length === 0) return true;
    return (
      (d.referencia ?? '').toLowerCase().includes(q) ||
      d.cuenta.toLowerCase().includes(q) ||
      (d.clienteNombre ?? '').toLowerCase().includes(q)
    );
  });

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Landmark className="h-10 w-10" />}
        title={q ? `Ningún depósito coincide con "${q}".` : 'No hay depósitos sin identificar.'}
        description="Tesorería registra el depósito como cobro de cliente; aparece aquí hasta que se identifica con el remittance."
      />
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <Table className="w-full text-sm">
        <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
          <TableRow>
            <TableHead className="px-3 py-2 font-medium">Fecha valor</TableHead>
            <TableHead className="px-3 py-2 text-right font-medium">Días en espera</TableHead>
            <TableHead className="px-3 py-2 font-medium">Depósito</TableHead>
            <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
            <TableHead className="px-3 py-2 text-right font-medium">Monto</TableHead>
            <TableHead className="px-3 py-2 text-right font-medium">Por identificar</TableHead>
            <TableHead className="px-3 py-2" />
          </TableRow>
        </TableHeader>
        <TableBody className="divide-y">
          {items.map((d) => {
            const dias = diasEnEspera(d.fechaValor);
            const atrasado = dias > DIAS_ESPERA_ALERTA;
            return (
            <TableRow key={d.movimientoBancarioId} className="h-12 hover:bg-surface-subtle">
              <TableCell className="px-3 py-2 font-mono text-xs">{d.fechaValor.slice(0, 10)}</TableCell>
              <TableCell className="px-3 py-2 text-right tabular-nums">
                <span
                  className={cn(atrasado && 'rounded bg-warning-bg px-1.5 py-0.5 font-medium text-warning-fg')}
                  title={atrasado ? `Más de ${DIAS_ESPERA_ALERTA} días sin identificar` : undefined}
                >
                  {dias}
                </span>
              </TableCell>
              <TableCell className="px-3 py-2">
                <p className="font-mono text-xs">{d.referencia ?? 'Sin referencia'}</p>
                <p className="text-2xs text-ink-muted">{d.cuenta}</p>
              </TableCell>
              <TableCell className="max-w-56 truncate px-3 py-2">
                {d.clienteNombre ?? <span className="text-ink-muted">Sin identificar</span>}
              </TableCell>
              <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                {formatoMonto(d.monto, d.moneda)}
              </TableCell>
              <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                {formatoMonto(d.disponible, d.moneda)}
              </TableCell>
              <TableCell className="px-3 py-2 text-right">
                {onIdentificar && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => onIdentificar(d)}
                    aria-label={`Identificar depósito ${d.referencia ?? d.movimientoBancarioId}`}
                  >
                    Identificar
                  </Button>
                )}
              </TableCell>
            </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
