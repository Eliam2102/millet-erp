import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
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
  PaginacionTabla,
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
import { Checkbox } from '@/components/ui/checkbox';
import { Field } from '@/features/almacen/components/internal/Field';
import { BusquedaFolio } from '@/features/almacen/components/internal/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/features/almacen/lib/paginacion';
import { RegularizacionValeBadge } from '../components/RegularizacionValeBadge';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

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
 * <c>almacen.salidas.leer-todas</c>. El backend no filtra "propias"
 * para salidas.
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
    soloVencidos: search.soloVencidos,
    soloPorVencer: search.soloPorVencer,
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  function actualizarSearch(parcial: Partial<SalidasSearch>) {
    navigate({
      to: '/almacen/salidas',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Salidas"
        descripcion="Surtido de requisiciones aprobadas y vales urgentes (A14). El comprobante PDF se descarga del detalle."
        acciones={
          <>
            {(puedeRegistrar || puedeRegistrarVale) && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Nueva salida
              </Button>
            )}
          </>
        }
      />

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
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin salidas"
          description={
            search.q ||
            search.estado != null ||
            search.subAlmacenId ||
            search.rqId ||
            search.desde ||
            search.hasta ||
            search.soloVales ||
            search.noRegularizados ||
            search.soloVencidos ||
            search.soloPorVencer
              ? 'No hay salidas que coincidan con los filtros aplicados.'
              : 'Aún no se han registrado salidas. Usa "Nueva salida" para capturar la primera.'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TablaSalidas items={items} subAlmacenesMap={subAlmacenesMap} />
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
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
    search.noRegularizados || search.soloVencidos || search.soloPorVencer;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <Field label="Folio" className="gap-1">
        <BusquedaFolio
          aria-label="Buscar por folio"
          valor={search.q}
          onCommit={(q) => onChange({ q })}
          placeholder="Buscar por folio…"
          className="w-56"
        />
      </Field>
      <Field label="Estado" className="gap-1">
        {(c) => (
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
            <SelectTrigger {...c} aria-label="Filtrar por estado" className="w-44">
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
        )}
      </Field>
      <Field label="Sub-almacén" className="gap-1">
        {(c) => (
          <Select
            value={search.subAlmacenId ?? SENTINEL_ALL}
            onValueChange={(v) =>
              onChange({ subAlmacenId: v === SENTINEL_ALL ? undefined : v })
            }
          >
            <SelectTrigger {...c} aria-label="Filtrar por sub-almacén" className="w-56">
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
        )}
      </Field>
      <Field label="Desde" className="gap-1">
        <Input
          aria-label="Fecha desde"
          type="date"
          value={search.desde ?? ''}
          onChange={(e) => onChange({ desde: e.target.value || undefined })}
          className="w-40"
        />
      </Field>
      <Field label="Hasta" className="gap-1">
        <Input
          aria-label="Fecha hasta"
          type="date"
          value={search.hasta ?? ''}
          onChange={(e) => onChange({ hasta: e.target.value || undefined })}
          className="w-40"
        />
      </Field>
      <div className="flex items-center gap-2 pb-1.5">
        <Checkbox
          id="solo-vales"
          checked={search.soloVales ?? false}
          onCheckedChange={(checked) =>
            onChange({ soloVales: checked === true || undefined })
          }
        />
        <label htmlFor="solo-vales" className="text-sm">
          Solo vales urgentes
        </label>
      </div>
      <div className="flex items-center gap-2 pb-1.5">
        <Checkbox
          id="no-regularizados"
          checked={search.noRegularizados ?? false}
          onCheckedChange={(checked) =>
            onChange({
              noRegularizados: checked === true || undefined,
              // Implícito: si filtramos no-regularizados, hace falta tipo vale.
              soloVales: checked === true ? true : search.soloVales,
            })
          }
        />
        <label htmlFor="no-regularizados" className="text-sm">
          Solo pendientes de regularizar
        </label>
      </div>
      <div className="flex items-center gap-2 pb-1.5">
        <Checkbox
          id="solo-vencidos"
          checked={search.soloVencidos ?? false}
          onCheckedChange={(checked) =>
            onChange({ soloVencidos: checked === true || undefined })
          }
        />
        <label htmlFor="solo-vencidos" className="text-sm">
          Solo vales vencidos
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
              soloVencidos: undefined,
              soloPorVencer: undefined,
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
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>
              <SortableHeader
                columnKey="folio"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>Tipo</TableHead>
            <TableHead>
              <SortableHeader
                columnKey="fechaMovimiento"
                label="Fecha"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="subAlmacen"
                label="Sub-almacén"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>RQ</TableHead>
            <TableHead className="text-right">
              <SortableHeader
                columnKey="montoTotalMxn"
                label="Monto"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {ordenados.map((r) => {
            const rowProps = clickableRowProps(() =>
              navigate({
                to: '/almacen/salidas/$id',
                params: { id: r.id },
              }),
            );
            return (
              <TableRow
                key={r.id}
                {...rowProps}
                className={`hover:bg-surface-subtle ${rowProps.className}`}
              >
                <TableCell className="font-mono text-xs">
                  <Link
                    to="/almacen/salidas/$id"
                    params={{ id: r.id }}
                    onClick={(e) => e.stopPropagation()}
                    className="text-brand hover:underline"
                    aria-label={`Abrir salida ${r.folio}`}
                  >
                    {r.folio}
                  </Link>
                </TableCell>
                <TableCell>
                  {r.esPorVale ? (
                    <RegularizacionValeBadge salida={r} />
                  ) : (
                    <span className="text-xs text-ink-muted">RQ</span>
                  )}
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  <DateTimeDisplay value={r.fechaMovimiento} variant="corta" />
                </TableCell>
                <TableCell className="font-mono text-xs">{r.subAlmacen}</TableCell>
                <TableCell className="font-mono text-xs text-ink-muted">
                  {r.rqFolio ? (
                    r.rqFolio
                  ) : r.rqRegularizadoraFolio ? (
                    <>Vale · {r.rqRegularizadoraFolio}</>
                  ) : (
                    <span className="italic">—</span>
                  )}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={r.montoTotalMxn} currency="MXN" strong />
                </TableCell>
                <TableCell>
                  <EstadoAlmacenBadge tipo="movimiento" estado={r.estado} />
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
