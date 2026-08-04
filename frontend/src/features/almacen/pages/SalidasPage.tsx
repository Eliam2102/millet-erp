import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  EmptyState,
  ErrorState,
  SortableHeader,
  TableSkeleton,
  clickableRowProps,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { mapById } from '@/features/catalogos/api';
import { useSalidas } from '@/features/almacen/api/useSalidas';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import {
  EstadoMovimiento,
  EstadoMovimientoLabels,
  type SalidaListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { NuevaSalidaSheet } from '@/features/almacen/components/NuevaSalidaSheet';
import type { SalidasSearch } from '@/features/almacen/lib/salidas-search-schema';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/salidas/' as const;
const SENTINEL_ALL = '__all__';

type SortKey =
  | 'folio'
  | 'fechaMovimiento'
  | 'subAlmacen'
  | 'rqId'
  | 'montoTotalMxn'
  | 'estado';

/**
 * <c>P3 — Bandeja de salidas</c> (doc 07 §FE-F3-PR1). Tabla con
 * filtros (folio, estado, sub-almacén, RQ, rango de fechas, vales
 * únicamente). Botón "Nueva salida" abre el sheet con switch
 * Variante A (con RQ) / B (vale urgente). Gateada a nivel ruta por
 * <c>almacen.salidas.leer-todas</c> (o leer-propias — el backend
 * filtra por usuario en ese caso).
 */
export function SalidasPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.AlmacenSalidasRegistrar,
  );
  const puedeRegistrarVale = useHasPermission(
    PermisosCanonicos.AlmacenSalidasPorVale,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const subAlmacenesMap = useMemo(
    () => mapById(subAlmacenesQuery.data?.items),
    [subAlmacenesQuery.data],
  );

  const query = useSalidas({
    estado: search.estado,
    subAlmacenId: search.subAlmacenId,
    rqId: search.rqId,
    desde: search.desde,
    hasta: search.hasta,
    soloVales: search.soloVales,
    noRegularizados: search.noRegularizados,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<SalidasSearch>) {
    navigate({
      to: '/almacen/salidas',
      search: { ...search, ...parcial },
    });
  }

  const itemsFiltrados = useMemo(() => {
    const items = query.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter((s) => s.folio.toLowerCase().includes(needle));
  }, [query.data, search.q]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Salidas</h1>
          <p className="text-sm text-muted-foreground">
            Surtido de requisiciones aprobadas y vales urgentes (A14).
            El comprobante PDF se descarga del detalle.
          </p>
        </div>
        {(puedeRegistrar || puedeRegistrarVale) && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva salida
          </Button>
        )}
      </div>

      <FiltrosToolbar
        search={search}
        subAlmacenes={subAlmacenesQuery.data?.items ?? []}
        onChange={actualizarSearch}
      />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las salidas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={8}
          columns={[
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-24' },
          ]}
        />
      ) : itemsFiltrados.length === 0 ? (
        <EmptyState
          title="Sin salidas"
          description={
            search.q ||
            search.estado != null ||
            search.subAlmacenId ||
            search.rqId ||
            search.desde ||
            search.hasta ||
            search.soloVales
              ? 'No hay salidas que coincidan con los filtros aplicados.'
              : 'Aún no se han registrado salidas. Usa "Nueva salida" para capturar la primera.'
          }
        />
      ) : (
        <TablaSalidas
          items={itemsFiltrados}
          subAlmacenesMap={subAlmacenesMap}
        />
      )}

      <NuevaSalidaSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
        puedeRegistrarConRq={puedeRegistrar}
        puedeRegistrarVale={puedeRegistrarVale}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: SalidasSearch;
  subAlmacenes: readonly { id: string; clave: string; nombre: string }[];
  onChange: (parcial: Partial<SalidasSearch>) => void;
}

function FiltrosToolbar({
  search,
  subAlmacenes,
  onChange,
}: FiltrosToolbarProps) {
  const algunFiltro =
    search.q ||
    search.estado != null ||
    search.subAlmacenId ||
    search.rqId ||
    search.desde ||
    search.hasta ||
    search.soloVales ||
    search.noRegularizados;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Folio</label>
        <Input
          aria-label="Buscar por folio"
          value={search.q ?? ''}
          onChange={(e) => onChange({ q: e.target.value || undefined })}
          placeholder="Buscar por folio…"
          className="w-56"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as EstadoMovimiento),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {(
              [
                EstadoMovimiento.Borrador,
                EstadoMovimiento.Validado,
                EstadoMovimiento.Registrado,
                EstadoMovimiento.Cancelado,
              ] as const
            ).map((e) => (
              <SelectItem key={e} value={String(e)}>
                {EstadoMovimientoLabels[e]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Sub-almacén</label>
        <Select
          value={search.subAlmacenId ?? SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({ subAlmacenId: v === SENTINEL_ALL ? undefined : v })
          }
        >
          <SelectTrigger aria-label="Filtrar por sub-almacén" className="w-56">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {subAlmacenes.map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.clave} · {s.nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Desde</label>
        <Input
          aria-label="Fecha desde"
          type="date"
          value={search.desde ?? ''}
          onChange={(e) => onChange({ desde: e.target.value || undefined })}
          className="w-40"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Hasta</label>
        <Input
          aria-label="Fecha hasta"
          type="date"
          value={search.hasta ?? ''}
          onChange={(e) => onChange({ hasta: e.target.value || undefined })}
          className="w-40"
        />
      </div>
      <div className="flex items-center gap-2 pb-1">
        <input
          id="solo-vales"
          type="checkbox"
          checked={search.soloVales ?? false}
          onChange={(e) =>
            onChange({ soloVales: e.target.checked || undefined })
          }
          className="h-4 w-4"
        />
        <label htmlFor="solo-vales" className="text-sm">
          Solo vales urgentes
        </label>
      </div>
      <div className="flex items-center gap-2 pb-1">
        <input
          id="no-regularizados"
          type="checkbox"
          checked={search.noRegularizados ?? false}
          onChange={(e) =>
            onChange({
              noRegularizados: e.target.checked || undefined,
              // Implícito: si filtramos no-regularizados, hace falta tipo vale.
              soloVales: e.target.checked ? true : search.soloVales,
            })
          }
          className="h-4 w-4"
        />
        <label htmlFor="no-regularizados" className="text-sm">
          Solo pendientes de regularizar (A14)
        </label>
      </div>
      {algunFiltro && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
              estado: undefined,
              subAlmacenId: undefined,
              rqId: undefined,
              desde: undefined,
              hasta: undefined,
              soloVales: undefined,
              noRegularizados: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaSalidasProps {
  items: readonly SalidaListItem[];
  subAlmacenesMap: Map<string, { id: string; clave: string; nombre: string }>;
}

function TablaSalidas({ items, subAlmacenesMap }: TablaSalidasProps) {
  const navigate = useNavigate();
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((r) => ({
        ...r,
        subAlmacen:
          subAlmacenesMap.get(r.subAlmacenId)?.clave ?? r.subAlmacenId,
      })),
    [items, subAlmacenesMap],
  );

  const ordenados = useMemo(() => {
    if (sort == null) return enriquecidos;
    return [...enriquecidos].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [enriquecidos, sort]);

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="folio"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">Tipo</th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaMovimiento"
                label="Fecha"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="subAlmacen"
                label="Sub-almacén"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">RQ</th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="montoTotalMxn"
                label="Monto"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </th>
          </tr>
        </thead>
        <tbody>
          {ordenados.map((r) => {
            const rowProps = clickableRowProps(() =>
              navigate({
                to: '/almacen/salidas/$id',
                params: { id: r.id },
              }),
            );
            return (
            <tr
              key={r.id}
              {...rowProps}
              className={`border-t hover:bg-muted/30 ${rowProps.className}`}
              aria-label={`Abrir salida ${r.folio}`}
            >
              <td className="px-3 py-2 font-mono text-xs text-primary">
                {r.folio}
              </td>
              <td className="px-3 py-2">
                {r.esPorVale ? (
                  <ValeBadge
                    fechaMovimiento={r.fechaMovimiento}
                    rqRegularizadoraId={r.rqRegularizadoraId}
                  />
                ) : (
                  <span className="text-xs text-muted-foreground">RQ</span>
                )}
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {r.fechaMovimiento}
              </td>
              <td className="px-3 py-2 font-mono text-xs">{r.subAlmacen}</td>
              <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
                {r.rqFolio ? (
                  r.rqFolio
                ) : r.rqRegularizadoraFolio ? (
                  <>Vale · {r.rqRegularizadoraFolio}</>
                ) : (
                  <span className="italic">—</span>
                )}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(r.montoTotalMxn)}
              </td>
              <td className="px-3 py-2">
                <EstadoMovimientoBadge estado={r.estado} />
              </td>
            </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function EstadoMovimientoBadge({ estado }: { estado: EstadoMovimiento }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoMovimiento.Borrador && 'bg-slate-200 text-slate-700',
        estado === EstadoMovimiento.Validado && 'bg-blue-100 text-blue-800',
        estado === EstadoMovimiento.Registrado &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoMovimiento.Cancelado && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoMovimientoLabels[estado]}
    </span>
  );
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
}


/**
 * <c>&lt;ValeBadge/&gt;</c> — visual cue para salidas tipo vale:
 * - Verde "Vale ✓" si ya fue regularizada con RQ posterior.
 * - Ámbar "Vale (Xh)" si lleva &lt; 24h sin regularizar.
 * - Rojo "Vale Xd!" si lleva ≥ 48h sin regularizar (excede SLA A14).
 */
function ValeBadge({
  fechaMovimiento,
  rqRegularizadoraId,
}: {
  fechaMovimiento: string;
  rqRegularizadoraId: string | null;
}) {
  if (rqRegularizadoraId) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
        Vale ✓
      </span>
    );
  }
  const horasAbiertas = horasDesde(fechaMovimiento);
  if (horasAbiertas >= 48) {
    const dias = Math.floor(horasAbiertas / 24);
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2 py-0.5 text-xs font-semibold text-rose-800">
        Vale {dias}d!
      </span>
    );
  }
  if (horasAbiertas >= 24) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-amber-200 px-2 py-0.5 text-xs font-medium text-amber-900">
        Vale ({Math.floor(horasAbiertas)}h)
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
      Vale ({Math.floor(horasAbiertas)}h)
    </span>
  );
}

function horasDesde(fechaIso: string): number {
  // fechaIso es DateOnly YYYY-MM-DD; convertimos a medianoche local.
  const inicio = new Date(`${fechaIso}T00:00:00`);
  const ms = Date.now() - inicio.getTime();
  return ms / (1000 * 60 * 60);
}
