import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { Inbox } from 'lucide-react';
import type { FacturaAbiertaItem } from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { ordenarPorAntiguedad } from '@/features/cxc/lib/sugerir-por-antiguedad';
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
  // V61: de la más antigua a la más reciente.
  const items = ordenarPorAntiguedad(query.data ?? []);
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
      <Table className="w-full text-sm">
        <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
          <TableRow>
            <TableHead className="w-10 px-3 py-2" aria-label="Seleccionar" />
            <TableHead className="px-3 py-2 font-medium">Folio</TableHead>
            <TableHead className="px-3 py-2 font-medium">Vence</TableHead>
            <TableHead className="px-3 py-2 text-right font-medium">Saldo</TableHead>
            <TableHead className="px-3 py-2 text-right font-medium">Importe a aplicar</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody className="divide-y">
          {items.map((f) => {
            const marcada = seleccion.has(f.uuid);
            const importe = seleccion.get(f.uuid);
            // Lo que aún puede aplicarse: saldo menos propuestas abiertas (C7.4).
            const tope = f.disponible ?? f.saldo;
            return (
              <TableRow key={f.uuid} className={cn('align-middle', marcada && 'bg-brand/5')}>
                <TableCell className="px-3 py-2">
                  <Checkbox
                    checked={marcada}
                    disabled={disabled}
                    aria-label={`Aplicar a ${f.folio}`}
                    onCheckedChange={(v) => onCambiar(f.uuid, v === true ? tope : null)}
                  />
                </TableCell>
                <TableCell className="px-3 py-2">
                  <p className="font-mono">{f.folio}</p>
                </TableCell>
                <TableCell className="px-3 py-2 text-xs text-ink-muted">
                  {f.fechaVencimiento.slice(0, 10)}
                  <p className="text-2xs">{f.metodoPago}</p>
                </TableCell>
                <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                  {formatoMonto(f.saldo, f.moneda)}
                  {tope !== f.saldo && (
                    <p className="text-2xs text-ink-muted">
                      Disponible {formatoMonto(tope, f.moneda)}
                    </p>
                  )}
                </TableCell>
                <TableCell className="px-3 py-2 text-right">
                  <Input
                    type="number"
                    step="0.01"
                    min="0"
                    max={tope}
                    disabled={disabled || !marcada}
                    value={marcada && importe != null ? importe : ''}
                    aria-label={`Importe aplicado a ${f.folio}`}
                    className="ml-auto w-36 text-right font-mono"
                    onChange={(e) => {
                      const n = Number(e.target.value);
                      onCambiar(f.uuid, e.target.value === '' || Number.isNaN(n) ? 0 : n);
                    }}
                  />
                  {marcada && importe != null && importe > tope && (
                    <p className="mt-0.5 text-right text-2xs text-danger-fg">
                      Excede el saldo de la factura.
                    </p>
                  )}
                  {/* C7.4-a: en PPD se muestra (solo consulta) la parcialidad siguiente y el saldo
                      insoluto después de este pago; Facturación asigna el número al timbrar. */}
                  {marcada && importe != null && importe > 0 && importe <= tope && f.metodoPago === 'PPD' && (
                    <p className="mt-0.5 text-right text-2xs text-ink-muted">
                      {f.numParcialidadSiguiente != null
                        ? `Parc. ${f.numParcialidadSiguiente}`
                        : 'Parcialidad'}{' '}
                      · saldo insoluto {formatoMonto(tope - importe, f.moneda)}
                    </p>
                  )}
                  {marcada && importe != null && importe > 0 && importe < tope && f.metodoPago !== 'PPD' && (
                    <p className="mt-0.5 text-right text-2xs text-danger-fg">
                      {f.metodoPago}: no admite parcialidades; aplica el saldo completo.
                    </p>
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
