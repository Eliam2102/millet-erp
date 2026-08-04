import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { Inbox } from 'lucide-react';
import type { FacturaAbiertaItem } from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/** Selección editable del matching: uuid → importe aplicado. */
export type SeleccionMatching = ReadonlyMap<string, number>;

/**
 * <c>&lt;MatchingAplicacionTable/&gt;</c> — lado derecho del matching
 * depósito ↔ facturas (05-frontend-diseno §4.1, componente nuevo §5).
 * Facturas vivas del cliente con checkbox + importe editable INLINE
 * (parcialidades) — nunca modal (§6.3). El footer con Σ aplicado /
 * diferencia / aviso de tolerancia lo pinta el caller (necesita el
 * monto del depósito).
 */
export interface MatchingAplicacionTableProps {
  query: {
    data: FacturaAbiertaItem[] | undefined;
    isLoading: boolean;
    isError: boolean;
    error: unknown;
    refetch: () => void;
  };
  seleccion: SeleccionMatching;
  onCambiar: (uuid: string, importe: number | null) => void;
  disabled?: boolean;
}

export function MatchingAplicacionTable({
  query,
  seleccion,
  onCambiar,
  disabled,
}: MatchingAplicacionTableProps) {
  if (query.isError) {
    return (
      <ErrorState
        title="No se pudieron cargar las facturas abiertas"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  if (query.isLoading) {
    return (
      <TableSkeleton
        rows={4}
        columns={[
          { width: 'w-8' },
          { width: 'w-28' },
          { width: 'w-28' },
          { width: 'w-28' },
          { width: 'w-32' },
        ]}
      />
    );
  }
  const items = query.data ?? [];
  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title="El cliente no tiene facturas abiertas en esa moneda."
        description="Solo facturas Abierta/Parcial de la cartera participan del matching."
      />
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
          <tr>
            <th className="w-10 px-3 py-2" aria-label="Seleccionar" />
            <th className="px-3 py-2 font-medium">Folio</th>
            <th className="px-3 py-2 font-medium">Vence</th>
            <th className="px-3 py-2 text-right font-medium">Saldo</th>
            <th className="px-3 py-2 text-right font-medium">
              Importe a aplicar
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((f) => {
            const marcada = seleccion.has(f.uuid);
            const importe = seleccion.get(f.uuid);
            return (
              <tr
                key={f.uuid}
                className={cn('align-middle', marcada && 'bg-primary/5')}
              >
                <td className="px-3 py-2">
                  <Checkbox
                    checked={marcada}
                    disabled={disabled}
                    aria-label={`Aplicar a ${f.folio}`}
                    onCheckedChange={(v) =>
                      onCambiar(f.uuid, v === true ? f.saldo : null)
                    }
                  />
                </td>
                <td className="px-3 py-2">
                  <p className="font-mono">{f.folio}</p>
                </td>
                <td className="px-3 py-2 text-xs text-muted-foreground">
                  {f.fechaVencimiento.slice(0, 10)}
                  <p className="text-[10px]">{f.metodoPago}</p>
                </td>
                <td className="px-3 py-2 text-right font-mono tabular-nums">
                  {formatoMonto(f.saldo, f.moneda)}
                </td>
                <td className="px-3 py-2 text-right">
                  <Input
                    type="number"
                    step="0.01"
                    min="0"
                    max={f.saldo}
                    disabled={disabled || !marcada}
                    value={marcada && importe != null ? importe : ''}
                    aria-label={`Importe aplicado a ${f.folio}`}
                    className="ml-auto w-36 text-right font-mono"
                    onChange={(e) => {
                      const n = Number(e.target.value);
                      onCambiar(
                        f.uuid,
                        e.target.value === '' || Number.isNaN(n) ? 0 : n,
                      );
                    }}
                  />
                  {marcada && importe != null && importe > f.saldo && (
                    <p className="mt-0.5 text-right text-[11px] text-destructive">
                      Excede el saldo de la factura.
                    </p>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
