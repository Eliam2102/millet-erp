import {
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
  TabsEstado,
} from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { ExcepcionImportacionItem } from '@/features/facturacion/api/types';
import { useExcepciones, useResolverExcepcion } from '@/features/facturacion/api/usePedidos';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import { ETIQUETA_ORIGEN_PEDIDO } from '@/features/facturacion/lib/glosario';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { CheckCircle2 } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

/**
 * <c>Bandeja de excepciones de ingesta</c> (FE-F3). Pedidos que fallaron
 * la importación A+W / Planta Pintura, por motivo. Resolución inline
 * (marca la excepción como resuelta tras corregir el dato maestro).
 */
export function BandejaExcepciones() {
  const [soloPendientes, setSoloPendientes] = useState(true);
  const puedeResolver = useHasPermission(PermisosCanonicos.FacturacionPedidosExcepcionesResolver);
  const query = useExcepciones(soloPendientes);
  const [busqueda, setBusqueda] = useState('');
  const [vista, setVista] = useState<number | undefined>(1);
  const items = (query.data ?? []).filter(
    (e) =>
      (vista !== 2 || e.resuelto) &&
      (e.pedidoRef + ' ' + e.motivo + ' ' + (e.detalle ?? ''))
        .toLowerCase()
        .includes(busqueda.toLowerCase()),
  );
  const paginacion = usePaginacionLocal(items, busqueda + vista);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <PageHeader
            titulo={<>Excepciones de ingesta</>}
            descripcion={
              <>
                Pedidos que fallaron la importación (cliente sin alta, producto sin clave SAT,
                almacén no asignado…). Corrige el dato y márcala resuelta.
              </>
            }
          />
        </div>
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 1, label: 'Pendiente' },
              { valor: 2, label: 'Resuelta' },
            ]}
            valor={vista}
            conteos={undefined}
            onChange={(v) => {
              setVista(v);
              setSoloPendientes(v === 1);
            }}
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Buscar pedido o motivo"
                placeholder="Pedido o motivo"
                value={busqueda}
                onChange={(e) => setBusqueda(e.target.value)}
              />
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={paginacion.offset}
            limit={10}
            tamanos={[]}
            onChange={paginacion.onChange}
          />
        }
      >
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
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Origen</TableHead>
                  <TableHead className="px-3 py-2 text-left">Pedido</TableHead>
                  <TableHead className="px-3 py-2 text-left">Motivo</TableHead>
                  <TableHead className="px-3 py-2 text-left">Detalle</TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginacion.visibles.map((e) => (
                  <FilaExcepcion key={e.id} excepcion={e} puedeResolver={puedeResolver} />
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
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
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo resolver.'),
      },
    );
  }

  return (
    <TableRow className="border-t hover:bg-surface-muted/30 border-line">
      <TableCell className="px-3 py-2">
        {ETIQUETA_ORIGEN_PEDIDO[excepcion.origen] ?? excepcion.origen}
      </TableCell>
      <TableCell className="px-3 py-2 font-mono text-xs">{excepcion.pedidoRef}</TableCell>
      <TableCell className="px-3 py-2">{excepcion.motivo}</TableCell>
      <TableCell className="px-3 py-2 text-xs text-ink-muted">{excepcion.detalle ?? '—'}</TableCell>
      <TableCell className="px-3 py-2">
        {excepcion.resuelto ? (
          <EstadoFacturacionBadge entidad="excepcion" estado="Resuelta" />
        ) : (
          <EstadoFacturacionBadge entidad="excepcion" estado="Pendiente" />
        )}
      </TableCell>
      <TableCell className="px-3 py-2 text-right tabular-nums">
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
      </TableCell>
    </TableRow>
  );
}
