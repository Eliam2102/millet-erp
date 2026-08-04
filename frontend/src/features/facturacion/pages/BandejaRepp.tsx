import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { BadgeSinAsignar } from '@/features/facturacion/components/BadgeSinAsignar';
import { useListarRepp } from '@/features/facturacion/api/useRepp';
import { useNuevoRepp } from '@/features/facturacion/components/nuevo-repp-context';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoTimbrado } from '@/features/facturacion/api/types';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { ReppSearch } from '@/features/facturacion/lib/repp-search-schema';

const FROM = '/_app/facturacion/repp/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Bandeja de complementos de pago (REPP)</c> (FE-F6). Lista los REPP
 * con su estado de timbrado; "Ver" abre el detalle (facturas cubiertas).
 * "Nuevo REPP" abre el Sheet de emisión.
 */
export function BandejaRepp() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nuevoRepp = useNuevoRepp();
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionReppEmitir);

  const query = useListarRepp(search.estado, search.alcance);

  function actualizarSearch(parcial: Partial<ReppSearch>) {
    navigate({ to: '/facturacion/repp', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((r) => {
    if (q.length === 0) return true;
    return (
      r.folio.toLowerCase().includes(q) ||
      (r.uuid ?? '').toLowerCase().includes(q) ||
      r.receptorNombre.toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Complementos de pago (REPP)
          </h1>
          <p className="text-sm text-muted-foreground">
            Recibos electrónicos de pago (Pago 2.0) emitidos.
          </p>
        </div>
        {puedeEmitir && (
          <Button onClick={() => nuevoRepp.abrir()}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo REPP
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Estado</label>
          <Select
            value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                estado:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as ReppSearch['estado']),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-48">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem value={String(EstadoTimbrado.Timbrado)}>Timbrado</SelectItem>
              <SelectItem value={String(EstadoTimbrado.TimbradoEnProceso)}>
                En proceso
              </SelectItem>
              <SelectItem value={String(EstadoTimbrado.TimbradoFallido)}>
                Fallido
              </SelectItem>
              <SelectItem value={String(EstadoTimbrado.Cancelado)}>
                Cancelado
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        {search.estado != null && (
          <Button variant="ghost" onClick={() => actualizarSearch({ estado: undefined })}>
            Limpiar
          </Button>
        )}
        <BadgeSinAsignar
          count={query.data?.sinAsignarCount ?? null}
          activo={search.alcance === 'sin-asignar'}
          onToggle={() =>
            actualizarSearch({
              alcance: search.alcance === 'sin-asignar' ? undefined : 'sin-asignar',
            })
          }
        />
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los complementos de pago"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-28' },
            { width: 'w-48' },
            { width: 'w-32' },
            { width: 'w-28' },
            { width: 'w-16' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin complementos de pago"
          description={
            search.estado != null || q.length > 0
              ? 'No hay REPP que coincidan con los filtros.'
              : 'Aún no hay complementos de pago. Emite uno con "Nuevo REPP".'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Folio</th>
                <th className="px-3 py-2 text-left">Receptor</th>
                <th className="px-3 py-2 text-right">Importe</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-left">Fecha pago</th>
                <th className="px-3 py-2 text-right">Acción</th>
              </tr>
            </thead>
            <tbody>
              {items.map((r) => (
                <tr key={r.id} className="border-t hover:bg-muted/30">
                  <td className="px-3 py-2 font-mono text-xs">{r.folio}</td>
                  <td className="px-3 py-2">{r.receptorNombre}</td>
                  <td className="px-3 py-2 text-right font-mono">
                    {r.importeTotalPago.toFixed(2)}
                  </td>
                  <td className="px-3 py-2">
                    <ChipTimbrado estado={r.estado} />
                  </td>
                  <td className="px-3 py-2 text-xs text-muted-foreground">
                    {new Date(r.fechaPago).toLocaleDateString('es-MX')}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Link
                      to="/facturacion/repp/$id"
                      params={{ id: r.id }}
                      className="text-sm font-medium text-primary hover:underline"
                    >
                      Ver
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
