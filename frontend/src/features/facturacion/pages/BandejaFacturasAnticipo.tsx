import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { BadgeSinAsignar } from '@/features/facturacion/components/BadgeSinAsignar';
import { useBandejaFacturasAnticipo } from '@/features/facturacion/api/useAnticipos';
import { esApiError } from '@/lib/api';
import { EstadoTimbrado } from '@/features/facturacion/api/types';
import { ETIQUETA_ESTADO_TIMBRADO } from '@/features/facturacion/lib/glosario';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { FacturasAnticipoSearch } from '@/features/facturacion/lib/facturas-anticipo-search-schema';

const FROM = '/_app/facturacion/anticipos/facturas/' as const;
const SENTINEL_ALL = '__all__';

const OPCIONES_ESTADO = [
  EstadoTimbrado.Borrador,
  EstadoTimbrado.TimbradoEnProceso,
  EstadoTimbrado.Timbrado,
  EstadoTimbrado.TimbradoFallido,
  EstadoTimbrado.CancelacionPendiente,
  EstadoTimbrado.Cancelado,
  EstadoTimbrado.Descartada,
] as const;

const NOMBRE_ESTADO: Record<number, string> = {
  [EstadoTimbrado.Borrador]: 'Borrador',
  [EstadoTimbrado.TimbradoEnProceso]: 'TimbradoEnProceso',
  [EstadoTimbrado.Timbrado]: 'Timbrado',
  [EstadoTimbrado.TimbradoFallido]: 'TimbradoFallido',
  [EstadoTimbrado.CancelacionPendiente]: 'CancelacionPendiente',
  [EstadoTimbrado.Cancelado]: 'Cancelado',
  [EstadoTimbrado.Descartada]: 'Descartada',
};

/**
 * <c>Bandeja de facturas de anticipo</c> (ANT-PR2, doc 13 §6 — patrón P1,
 * molde <c>BandejaFacturas</c>). Filtro por estado de timbrado server-side +
 * búsqueda client-side; columnas de saldo y estado del anticipo (13-H).
 * "Ver" abre el master-detail <c>/facturacion/anticipos/facturas/$id</c>.
 */
export function BandejaFacturasAnticipo() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const query = useBandejaFacturasAnticipo({
    estado: search.estado,
    limit: 200,
    alcance: search.alcance,
  });

  function actualizarSearch(parcial: Partial<FacturasAnticipoSearch>) {
    navigate({
      to: '/facturacion/anticipos/facturas',
      search: { ...search, ...parcial },
    });
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
          <h1 className="text-2xl font-semibold tracking-tight">
            Facturas de anticipo
          </h1>
          <p className="text-sm text-muted-foreground">
            CFDIs de anticipo (serie FANT) y el saldo amortizable de cada uno.
          </p>
        </div>
        <Button variant="outline" asChild>
          <Link to="/facturacion/anticipos">Control de Anticipos</Link>
        </Button>
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
                    : (Number(v) as FacturasAnticipoSearch['estado']),
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
          title="No se pudieron cargar las facturas de anticipo"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-28' },
            { width: 'w-48' },
            { width: 'w-24' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-16' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin facturas de anticipo"
          description={
            search.estado != null || q.length > 0
              ? 'No hay facturas de anticipo que coincidan con los filtros.'
              : 'Emite una desde Quick Create → Anticipo.'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Folio</th>
                <th className="px-3 py-2 text-left">Receptor</th>
                <th className="px-3 py-2 text-left">Tipo</th>
                <th className="px-3 py-2 text-right">Total</th>
                <th className="px-3 py-2 text-right">Saldo</th>
                <th className="px-3 py-2 text-left">CFDI</th>
                <th className="px-3 py-2 text-left">Anticipo</th>
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
                  <td className="px-3 py-2 text-xs">{f.tipoAnticipo}</td>
                  <td className="px-3 py-2 text-right font-mono">
                    {f.total.toFixed(2)} {f.moneda}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {f.saldo != null ? f.saldo.toFixed(2) : '—'}
                  </td>
                  <td className="px-3 py-2">
                    <ChipTimbrado estado={f.estado} />
                  </td>
                  <td className="px-3 py-2">
                    <ChipEstadoAnticipo estado={f.estadoAnticipo} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Link
                      to="/facturacion/anticipos/facturas/$id"
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

/** Chip del estado del saldo (Abierto / Amortizado / Cancelado, §6.6). */
export function ChipEstadoAnticipo({ estado }: { estado: string | null }) {
  if (estado == null) return <span className="text-xs text-muted-foreground">—</span>;
  const tono =
    estado === 'Abierto'
      ? 'bg-emerald-100 text-emerald-800'
      : estado === 'Amortizado'
        ? 'bg-sky-100 text-sky-800'
        : 'bg-zinc-200 text-zinc-700';
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${tono}`}
    >
      {estado}
    </span>
  );
}
