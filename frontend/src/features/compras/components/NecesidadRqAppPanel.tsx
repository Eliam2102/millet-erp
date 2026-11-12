import { PackageSearch } from 'lucide-react';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { NecesidadRqAppLinea } from '@/features/compras/api/types';

/**
 * <c>&lt;NecesidadRqAppPanel/&gt;</c> — M-332: necesidad de una RQ de la app de Almacenes por línea. La RQ ya viene descontada
 * por el jefe de almacén y se compra completa; el panel solo informa. Si el disponible cubre la necesidad avisa, pero Compras decide.
 */
export interface NecesidadRqAppPanelProps {
  lineas: readonly NecesidadRqAppLinea[];
  isLoading?: boolean;
}

function fmt(n: number): string {
  return n % 1 === 0 ? String(n) : n.toFixed(2);
}

export function NecesidadRqAppPanel({ lineas, isLoading = false }: NecesidadRqAppPanelProps) {
  const hayExistencia = lineas.some((l) => l.disponibleCubre);
  return (
    <section aria-label="Necesidad de la requisición de la app" className="rounded-md border p-3">
      <header className="flex items-center gap-2 text-sm font-medium">
        <PackageSearch className="h-4 w-4" />
        Necesidad de la requisición de la app
      </header>
      {hayExistencia && (
        <p
          role="status"
          className="mt-2 rounded-md border border-warning bg-warning-bg px-3 py-2 text-sm text-warning-fg"
        >
          Hay existencia suficiente; Compras decide.
        </p>
      )}
      {isLoading ? (
        <div className="mt-2 h-4 w-2/3 animate-pulse rounded bg-surface-muted" aria-hidden />
      ) : (
        <Table className="mt-2">
          <TableHeader>
            <TableRow>
              <TableHead>Artículo</TableHead>
              <TableHead className="text-right">Necesidad</TableHead>
              <TableHead className="text-right">Disponible hoy</TableHead>
              <TableHead className="text-right">Convertido a compra</TableHead>
              <TableHead className="text-right">Reducido</TableHead>
              <TableHead className="text-right">Pendiente</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {lineas.map((l) => (
              <TableRow key={l.lineaId} data-necesidad-linea={l.lineaId}>
                <TableCell>
                  {[l.articuloClave, l.articuloNombre].filter(Boolean).join(' · ') || l.articuloId}
                  {l.disponibleCubre && <span className="ml-2 text-xs text-warning-fg">Existencia suficiente</span>}
                </TableCell>
                <TableCell className="text-right font-mono">{fmt(l.necesidad)} {l.unidadMedida}</TableCell>
                <TableCell className="text-right font-mono">{fmt(l.disponible)}</TableCell>
                <TableCell className="text-right font-mono">{fmt(l.convertidoACompra)}</TableCell>
                <TableCell className="text-right font-mono">{fmt(l.reducido)}</TableCell>
                <TableCell className="text-right font-mono">{fmt(l.pendiente)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
