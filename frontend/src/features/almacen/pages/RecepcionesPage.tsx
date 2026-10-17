import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { FiltroActivoChip } from '../components/FiltroActivoChip';
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
import { useRecepciones } from '@/features/almacen/api/useRecepciones';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import {
  EstadoMovimiento,
  EstadoMovimientoLabels,
  type RecepcionListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { NuevaRecepcionSheet } from '@/features/almacen/components/NuevaRecepcionSheet';
import type { RecepcionesSearch } from '@/features/almacen/lib/recepciones-search-schema';
import { Field } from '@/features/almacen/components/internal/Field';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

const FROM = '/_app/almacen/recepciones/' as const;
const SENTINEL_ALL = '__all__';

type SortKey =
  | 'folio'
  | 'fechaMovimiento'
  | 'subAlmacen'
  | 'ordenCompraId'
  | 'montoTotalMxn'
  | 'estado';

/**
 * <c>P1 — Bandeja de recepciones</c> (doc 07 §FE-F2-PR1). Tabla con
 * filtros (estado, sub-almacén, OC, rango de fechas, búsqueda por
 * folio) y link a detalle por fila. Botón "Nueva recepción" abre el
 * sheet (próximo commit del PR) si el usuario tiene
 * <c>almacen.entradas.registrar</c>; oculto si no.
 *
 * <para>Gateada por <c>almacen.entradas.leer</c> a nivel ruta.</para>
 */
export function RecepcionesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.AlmacenEntradasRegistrar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const subAlmacenesMap = useMemo(
    () => mapById(subAlmacenesQuery.data?.items),
    [subAlmacenesQuery.data],
  );

  const query = useRecepciones({
    estado: search.estado,
    subAlmacenId: search.subAlmacenId,
    ordenCompraId: search.ordenCompraId,
    desde: search.desde,
    hasta: search.hasta,
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  function actualizarSearch(parcial: Partial<RecepcionesSearch>) {
    navigate({
      to: '/almacen/recepciones',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Recepciones"
        descripcion="Bandeja de entradas. Variante A (con factura) para insumos; Variante B (con packing list) para materiales directos."
        acciones={
          <>
            {puedeRegistrar && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Nueva recepción
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
          title="No se pudieron cargar las recepciones"
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
            { width: 'w-32' },
            { width: 'w-24' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin recepciones"
          description={
            search.q ||
            search.estado != null ||
            search.subAlmacenId ||
            search.ordenCompraId ||
            search.desde ||
            search.hasta
              ? 'No hay recepciones que coincidan con los filtros aplicados.'
              : 'Aún no se han registrado recepciones. Usa "Nueva recepción" para capturar la primera.'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TablaRecepciones items={items} subAlmacenesMap={subAlmacenesMap} />
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
      )}

      <NuevaRecepcionSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: RecepcionesSearch;
  subAlmacenes: readonly { id: string; clave: string; nombre: string }[];
  onChange: (parcial: Partial<RecepcionesSearch>) => void;
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
    search.ordenCompraId ||
    search.desde ||
    search.hasta;

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
      {search.ordenCompraId && (
        <FiltroActivoChip
          label="Orden de compra"
          onQuitar={() => onChange({ ordenCompraId: undefined })}
        />
      )}
      {algunFiltro && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
              estado: undefined,
              subAlmacenId: undefined,
              ordenCompraId: undefined,
              desde: undefined,
              hasta: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaRecepcionesProps {
  items: readonly RecepcionListItem[];
  subAlmacenesMap: Map<string, { id: string; clave: string; nombre: string }>;
}

function TablaRecepciones({ items, subAlmacenesMap }: TablaRecepcionesProps) {
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
            <TableHead>OC</TableHead>
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
                to: '/almacen/recepciones/$id',
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
                    to="/almacen/recepciones/$id"
                    params={{ id: r.id }}
                    onClick={(e) => e.stopPropagation()}
                    className="text-brand hover:underline"
                    aria-label={`Abrir recepción ${r.folio}`}
                  >
                    {r.folio}
                  </Link>
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  <DateTimeDisplay value={r.fechaMovimiento} variant="corta" />
                </TableCell>
                <TableCell className="font-mono text-xs">{r.subAlmacen}</TableCell>
                <TableCell className="text-xs">
                  {r.ordenCompraFolio ? (
                    <span className="font-mono">{r.ordenCompraFolio}</span>
                  ) : r.ordenCompraId ? (
                    <span className="font-mono text-ink-muted">
                      {truncarId(r.ordenCompraId)}
                    </span>
                  ) : (
                    <span className="italic text-ink-muted">—</span>
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

function truncarId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
