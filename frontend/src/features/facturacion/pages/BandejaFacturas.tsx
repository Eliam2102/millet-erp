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
import { useListarFacturas } from '@/features/facturacion/api/useFacturas';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoTimbrado } from '@/features/facturacion/api/types';
import { ETIQUETA_ESTADO_TIMBRADO } from '@/features/facturacion/lib/glosario';
import type { FacturasSearch } from '@/features/facturacion/lib/facturas-search-schema';
import { cn } from '@/lib/utils';

const FROM = '/_app/facturacion/facturas/' as const;
const SENTINEL_ALL = '__all__';

const OPCIONES_ESTADO = [
  EstadoTimbrado.Borrador,
  EstadoTimbrado.PendientePedimento,
  EstadoTimbrado.TimbradoEnProceso,
  EstadoTimbrado.Timbrado,
  EstadoTimbrado.TimbradoFallido,
  EstadoTimbrado.CancelacionPendiente,
  EstadoTimbrado.Cancelado,
  EstadoTimbrado.Descartada,
] as const;

const NOMBRE_ESTADO: Record<number, string> = {
  [EstadoTimbrado.Borrador]: 'Borrador',
  [EstadoTimbrado.PendientePedimento]: 'PendientePedimento',
  [EstadoTimbrado.TimbradoEnProceso]: 'TimbradoEnProceso',
  [EstadoTimbrado.Timbrado]: 'Timbrado',
  [EstadoTimbrado.TimbradoFallido]: 'TimbradoFallido',
  [EstadoTimbrado.CancelacionPendiente]: 'CancelacionPendiente',
  [EstadoTimbrado.Cancelado]: 'Cancelado',
  [EstadoTimbrado.Descartada]: 'Descartada',
};

/**
 * <c>Bandeja de comprobantes emitidos</c> (FE-F1-PR2). Tabla filtrada
 * por estado de timbrado (server-side) + búsqueda client-side por folio /
 * UUID / receptor. "Ver" lleva al detalle (master-detail §6.1). "Nueva
 * factura" navega al form de emisión con pestañas (FAC-UX-PR2).
 */
export function BandejaFacturas() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeEmitir = useHasPermission(
    PermisosCanonicos.FacturacionFacturasEmitir,
  );

  const query = useListarFacturas({
    estado: search.estado,
    limit: 200,
    alcance: search.alcance,
  });

  function actualizarSearch(parcial: Partial<FacturasSearch>) {
    navigate({ to: '/facturacion/facturas', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((f) => {
    if (q.length === 0) return true;
    return (
      f.folio.toLowerCase().includes(q) ||
      (f.uuid ?? '').toLowerCase().includes(q) ||
      f.receptorNombre.toLowerCase().includes(q) ||
      f.receptorRfc.toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Facturas</h1>
          <p className="text-sm text-muted-foreground">
            Comprobantes de venta emitidos y su estado de timbrado.
          </p>
        </div>
        {puedeEmitir && (
          <Button asChild>
            <Link to="/facturacion/facturas/nueva">
              <Plus className="mr-2 h-4 w-4" />
              Nueva factura
            </Link>
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
                    : (Number(v) as EstadoTimbrado),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-56">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              {OPCIONES_ESTADO.map((e) => (
                <SelectItem key={e} value={String(e)}>
                  {ETIQUETA_ESTADO_TIMBRADO[NOMBRE_ESTADO[e]] ?? NOMBRE_ESTADO[e]}
                </SelectItem>
              ))}
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
          title="No se pudieron cargar las facturas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-28' },
            { width: 'w-48' },
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-28' },
            { width: 'w-16' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin facturas"
          description={
            search.estado != null || q.length > 0
              ? 'No hay facturas que coincidan con los filtros.'
              : 'Aún no hay facturas emitidas. Emite una con "Nueva factura".'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Folio</th>
                <th className="px-3 py-2 text-left">Receptor</th>
                <th className="px-3 py-2 text-left">UUID</th>
                <th className="px-3 py-2 text-right">Total</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-right">Acción</th>
              </tr>
            </thead>
            <tbody>
              {items.map((f) => (
                <tr key={f.id} className="border-t hover:bg-muted/30">
                  <td className="px-3 py-2 font-mono text-xs">{f.folio}</td>
                  <td className="px-3 py-2">
                    <div>{f.receptorNombre}</div>
                    <div className="font-mono text-xs text-muted-foreground">
                      {f.receptorRfc}
                    </div>
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {f.uuid ? `${f.uuid.slice(0, 8)}…${f.uuid.slice(-4)}` : '—'}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {formatearMonto(f.total, f.moneda)}
                  </td>
                  <td className="px-3 py-2">
                    <ChipTimbrado estado={f.estado} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Link
                      to="/facturacion/facturas/$id"
                      params={{ id: f.id }}
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

export function ChipTimbrado({ estado }: { estado: string }) {
  const tono =
    estado === 'Timbrado'
      ? 'bg-emerald-100 text-emerald-800'
      : estado === 'TimbradoFallido'
        ? 'bg-rose-100 text-rose-800'
        : estado === 'Cancelado' || estado === 'Descartada'
          ? 'bg-zinc-200 text-zinc-700'
          : estado === 'TimbradoEnProceso' || estado === 'CancelacionPendiente'
            ? 'bg-amber-100 text-amber-800'
            : 'bg-sky-100 text-sky-800';
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        tono,
      )}
    >
      {ETIQUETA_ESTADO_TIMBRADO[estado] ?? estado}
    </span>
  );
}

function formatearMonto(v: number, moneda: string): string {
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return `${v.toFixed(2)} ${moneda}`;
  }
}
