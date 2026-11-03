import type { ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { AlertTriangle } from 'lucide-react';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
  TableContainer,
} from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { formatMoney } from '@/lib/money';
import type { PagoSinReflejarItem } from '@/features/cxc/api/types';

/** "yyyy-MM-dd" → "dd/mm/yyyy" sin pasar por zona horaria (es un DateOnly). */
function fechaCorta(iso: string): string {
  const [anio, mes, dia] = iso.split('-');
  return anio && mes && dia ? `${dia}/${mes}/${anio}` : iso;
}

/**
 * <c>&lt;PanelBloqueoEstadoCuenta/&gt;</c> — F1-CXC-09 · C7.8 (CA7.14): el
 * estado de cuenta no se emite ni se envía mientras haya pagos aplicados sin
 * REP o con REP aún no reflejado en cartera. Reemplaza al reporte (sin
 * exportar ni enviar) y lleva a la bandeja de REP pendientes de Facturación.
 */
export function PanelBloqueoEstadoCuenta({
  pendientes,
  isLoading,
  filtros,
}: {
  pendientes: PagoSinReflejarItem[] | undefined;
  isLoading: boolean;
  filtros: ReactNode;
}) {
  return (
    <section
      aria-labelledby="bloqueo-estado-cuenta"
      className="flex flex-col gap-4 rounded-md border border-line bg-surface-card p-4 shadow-card-flat"
    >
      <div data-print="hidden">{filtros}</div>
      <div role="alert" className="flex items-start gap-3 rounded-md bg-warning-bg p-3 text-warning-fg">
        <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <div className="space-y-1">
          <h2 id="bloqueo-estado-cuenta" className="text-sm font-semibold">
            No se puede emitir: hay pagos aplicados sin REP
          </h2>
          <p className="text-sm">
            El estado de cuenta se emite y se envía hasta que cada pago confirmado tenga su REP
            timbrado y reflejado en cartera. Emite o revisa estos pagos en la bandeja de REP
            pendientes y vuelve a generar el estado de cuenta.
          </p>
          <Link
            to="/facturacion/repp/pendientes"
            className="inline-block text-sm font-medium underline underline-offset-2"
          >
            Ir a REP pendientes
          </Link>
        </div>
      </div>

      {isLoading ? (
        <p role="status" className="text-sm text-ink-muted">
          Cargando pagos pendientes…
        </p>
      ) : pendientes && pendientes.length > 0 ? (
        <TableContainer maxHeightClassName="">
        <Table aria-label="Pagos que bloquean el estado de cuenta">
          <TableHeader>
            <TableRow>
              <TableHead>Fecha del pago</TableHead>
              <TableHead>Referencia</TableHead>
              <TableHead className="text-right">Monto</TableHead>
              <TableHead>Moneda</TableHead>
              <TableHead>Límite REP</TableHead>
              <TableHead>Situación</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {pendientes.map((p) => (
              <TableRow key={p.id}>
                <TableCell>{fechaCorta(p.fechaValor)}</TableCell>
                <TableCell className="truncate">{p.referencia ?? 'Sin referencia'}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {formatMoney({ amount: p.monto, currency: p.moneda })}
                </TableCell>
                <TableCell>{p.moneda}</TableCell>
                <TableCell>{fechaCorta(p.fechaLimite)}</TableCell>
                <TableCell>
                  <Badge variant={p.motivo === 'SinRep' ? 'warning' : 'info'}>
                    {p.motivo === 'SinRep' ? 'Sin REP' : 'REP sin reflejar en cartera'}
                  </Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        </TableContainer>
      ) : (
        <p className="text-sm text-ink-muted">
          No se pudo obtener el detalle de los pagos pendientes. Revisa la bandeja de REP
          pendientes.
        </p>
      )}
    </section>
  );
}
