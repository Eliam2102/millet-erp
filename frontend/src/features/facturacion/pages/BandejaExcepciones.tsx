import { useState } from 'react';
import { CheckCircle2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useExcepciones,
  useResolverExcepcion,
} from '@/features/facturacion/api/usePedidos';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { ETIQUETA_ORIGEN_PEDIDO } from '@/features/facturacion/lib/glosario';
import type { ExcepcionImportacionItem } from '@/features/facturacion/api/types';

/**
 * <c>Bandeja de excepciones de ingesta</c> (FE-F3). Pedidos que fallaron
 * la importación A+W / Planta Pintura, por motivo. Resolución inline
 * (marca la excepción como resuelta tras corregir el dato maestro).
 */
export function BandejaExcepciones() {
  const [soloPendientes, setSoloPendientes] = useState(true);
  const puedeResolver = useHasPermission(
    PermisosCanonicos.FacturacionPedidosExcepcionesResolver,
  );
  const query = useExcepciones(soloPendientes);
  const items = query.data ?? [];

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Excepciones de ingesta
          </h1>
          <p className="text-sm text-muted-foreground">
            Pedidos que fallaron la importación (cliente sin alta, producto sin
            clave SAT, almacén no asignado…). Corrige el dato y márcala resuelta.
          </p>
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4 rounded border-input"
            checked={soloPendientes}
            onChange={(e) => setSoloPendientes(e.target.checked)}
          />
          Solo pendientes
        </label>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las excepciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-48' },
            { width: 'w-28' },
            { width: 'w-24' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin excepciones"
          description={
            soloPendientes
              ? 'No hay excepciones pendientes. Todo lo importado entró correctamente.'
              : 'No hay excepciones registradas.'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Origen</th>
                <th className="px-3 py-2 text-left">Pedido</th>
                <th className="px-3 py-2 text-left">Motivo</th>
                <th className="px-3 py-2 text-left">Detalle</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-right">Acción</th>
              </tr>
            </thead>
            <tbody>
              {items.map((e) => (
                <FilaExcepcion
                  key={e.id}
                  excepcion={e}
                  puedeResolver={puedeResolver}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function FilaExcepcion({
  excepcion,
  puedeResolver,
}: {
  excepcion: ExcepcionImportacionItem;
  puedeResolver: boolean;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const resolver = useResolverExcepcion();

  function marcarResuelta() {
    resolver.mutate(
      { id: excepcion.id, idempotencyKey },
      {
        onSuccess: () => toast.success('Excepción marcada como resuelta.'),
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo resolver.',
          ),
      },
    );
  }

  return (
    <tr className="border-t hover:bg-muted/30">
      <td className="px-3 py-2">
        {ETIQUETA_ORIGEN_PEDIDO[excepcion.origen] ?? excepcion.origen}
      </td>
      <td className="px-3 py-2 font-mono text-xs">{excepcion.pedidoRef}</td>
      <td className="px-3 py-2">{excepcion.motivo}</td>
      <td className="px-3 py-2 text-xs text-muted-foreground">
        {excepcion.detalle ?? '—'}
      </td>
      <td className="px-3 py-2">
        {excepcion.resuelto ? (
          <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
            Resuelta
          </span>
        ) : (
          <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
            Pendiente
          </span>
        )}
      </td>
      <td className="px-3 py-2 text-right">
        {puedeResolver && !excepcion.resuelto && (
          <Button
            variant="outline"
            size="sm"
            onClick={marcarResuelta}
            disabled={resolver.isPending}
          >
            <CheckCircle2 className="mr-1 h-4 w-4" />
            {resolver.isPending ? 'Resolviendo…' : 'Marcar resuelta'}
          </Button>
        )}
      </td>
    </tr>
  );
}
