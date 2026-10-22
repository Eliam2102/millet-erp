import { KpiCard } from '@/components/erp/display/KpiCard';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
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
  BarraFiltros,
  FiltroActivoChip,
  PaginacionTabla,
  SortableHeader,
  TabsEstado,
  useConteosPorEstado,
  TableSkeleton,
  clickableRowProps,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { mapById } from '@/features/catalogos/api';
import { useRecepciones } from '@/features/almacen/api/useRecepciones';
import { almacenKeys } from '@/features/almacen/api/keys';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import {
  EstadoMovimiento,
  EstadoMovimientoLabels,
  type RecepcionListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { ActivosRecibidosOc } from '@/features/almacen/components/ActivosRecibidosOc';
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
  const puedeRegistrar = useHasPermission(PermisosCanonicos.AlmacenEntradasRegistrar);
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

  const conteos = useConteosPorEstado(almacenKeys.recepciones(), '/api/v1/almacen/recepciones', {
    subAlmacenId: search.subAlmacenId,
    ordenCompraId: search.ordenCompraId,
    desde: search.desde,
    hasta: search.hasta,
    q: search.q,
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
    <div className="space-y-5">
      <PageHeader
        titulo="Recepciones"
        descripcion="Bandeja de entradas. Variante A (con factura) para insumos; Variante B (con packing list) para materiales directos."
        acciones={
          <>
            {puedeRegistrar && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 size-3.5" />
                Nueva recepción
              </Button>
            )}
          </>
        }
      />

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {TABS_ESTADO.filter((tab) =>
          [EstadoMovimiento.Borrador, EstadoMovimiento.Validado].some(
            (estado) => estado === tab.valor,
          ),
        ).map((tab) => (
          <Button
            key={tab.valor}
            variant="ghost"
            className="h-auto items-stretch p-0 text-left [&>div]:w-full"
            aria-pressed={search.estado === tab.valor}
            onClick={() => actualizarSearch({ estado: tab.valor as typeof search.estado })}
          >
            <KpiCard etiqueta={tab.label} valor={conteos.data?.[tab.valor] ?? '—'} />
          </Button>
        ))}
      </div>
      {search.ordenCompraId && <ActivosRecibidosOc ordenCompraId={search.ordenCompraId} />}
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={TABS_ESTADO}
            valor={search.estado}
            onChange={(estado) =>
              actualizarSearch({ estado: estado as EstadoMovimiento | undefined })
            }
            conteos={conteos.data}
          />
        }
        filtros={
          <FiltrosToolbar
            search={search}
            subAlmacenes={subAlmacenesQuery.data?.items ?? []}
            total={query.data?.total}
            onChange={actualizarSearch}
          />
        }
        pie={
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        }
      >
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
          </div>
        )}
      </ListadoModulo>

      <NuevaRecepcionSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />
    </div>
  );
}

const TABS_ESTADO = [
  EstadoMovimiento.Borrador,
  EstadoMovimiento.Validado,
  EstadoMovimiento.Registrado,
  EstadoMovimiento.Cancelado,
].map((e) => ({ valor: e as number, label: EstadoMovimientoLabels[e] }));

interface FiltrosToolbarProps {
  search: RecepcionesSearch;
  subAlmacenes: readonly { id: string; clave: string; nombre: string }[];
  total: number | undefined;
  onChange: (parcial: Partial<RecepcionesSearch>) => void;
}

function FiltrosToolbar({ search, subAlmacenes, total, onChange }: FiltrosToolbarProps) {
  const algunFiltro =
    search.q || search.subAlmacenId || search.ordenCompraId || search.desde || search.hasta;

  return (
    <BarraFiltros
      aria-label="Filtros de recepciones"
      busqueda={
        <BusquedaFolio
          aria-label="Buscar por folio"
          valor={search.q}
          onCommit={(q) => onChange({ q })}
          placeholder="Folio de la recepción"
        />
      }
      filtros={
        <Select
          value={search.subAlmacenId ?? SENTINEL_ALL}
          onValueChange={(v) => onChange({ subAlmacenId: v === SENTINEL_ALL ? undefined : v })}
        >
          <SelectTrigger aria-label="Filtrar por sub-almacén" className="h-ctl-md w-48 text-xs">
            <SelectValue placeholder="Todos los sub-almacenes" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos los sub-almacenes</SelectItem>
            {subAlmacenes.map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.clave} · {s.nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      }
      masFiltros={
        <>
          <Field label="Desde">
            <Input
              type="date"
              value={search.desde ?? ''}
              onChange={(e) => onChange({ desde: e.target.value || undefined })}
            />
          </Field>
          <Field label="Hasta">
            <Input
              type="date"
              value={search.hasta ?? ''}
              onChange={(e) => onChange({ hasta: e.target.value || undefined })}
            />
          </Field>
        </>
      }
      masFiltrosActivos={(search.desde ? 1 : 0) + (search.hasta ? 1 : 0)}
      chips={
        search.ordenCompraId && (
          <FiltroActivoChip
            label="Orden de compra"
            onQuitar={() => onChange({ ordenCompraId: undefined })}
          />
        )
      }
      onLimpiar={
        algunFiltro
          ? () =>
              onChange({
                q: undefined,
                subAlmacenId: undefined,
                ordenCompraId: undefined,
                desde: undefined,
                hasta: undefined,
              })
          : undefined
      }
      total={total != null ? `${total} ${total === 1 ? 'recepción' : 'recepciones'}` : undefined}
    />
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
        subAlmacen: subAlmacenesMap.get(r.subAlmacenId)?.clave ?? r.subAlmacenId,
      })),
    [items, subAlmacenesMap],
  );

  const ordenados = useMemo(() => {
    if (sort == null) return enriquecidos;
    return [...enriquecidos].sort((a, b) => compareItemsBy(a, b, sort.key, sort.dir));
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
                    <span className="font-mono text-ink-muted">{truncarId(r.ordenCompraId)}</span>
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
