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
import { useSalidas } from '@/features/almacen/api/useSalidas';
import { almacenKeys } from '@/features/almacen/api/keys';
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
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { RegularizacionValeBadge } from '../components/RegularizacionValeBadge';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

const FROM = '/_app/almacen/salidas/' as const;
const SENTINEL_ALL = '__all__';

type SortKey = 'folio' | 'fechaMovimiento' | 'subAlmacen' | 'rqId' | 'montoTotalMxn' | 'estado';

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
  const puedeRegistrar = useHasPermission(PermisosCanonicos.AlmacenSalidasRegistrar);
  const puedeRegistrarVale = useHasPermission(PermisosCanonicos.AlmacenSalidasPorVale);
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

  const conteos = useConteosPorEstado(almacenKeys.salidas(), '/api/v1/almacen/salidas', {
    subAlmacenId: search.subAlmacenId,
    rqId: search.rqId,
    desde: search.desde,
    hasta: search.hasta,
    soloVales: search.soloVales,
    noRegularizados: search.noRegularizados,
    soloVencidos: search.soloVencidos,
    soloPorVencer: search.soloPorVencer,
    q: search.q,
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
    <div className="space-y-5">
      <PageHeader
        titulo="Salidas"
        descripcion="Surtido de requisiciones aprobadas y vales urgentes. El comprobante PDF se descarga del detalle."
        acciones={
          <>
            {(puedeRegistrar || puedeRegistrarVale) && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 size-3.5" />
                Nueva salida
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
          </div>
        )}
      </ListadoModulo>

      <NuevaSalidaSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
        puedeRegistrarConRq={puedeRegistrar}
        puedeRegistrarVale={puedeRegistrarVale}
      />
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
  search: SalidasSearch;
  subAlmacenes: readonly { id: string; clave: string; nombre: string }[];
  total: number | undefined;
  onChange: (parcial: Partial<SalidasSearch>) => void;
}

function FiltrosToolbar({ search, subAlmacenes, total, onChange }: FiltrosToolbarProps) {
  const algunFiltro =
    search.q ||
    search.subAlmacenId ||
    search.rqId ||
    search.desde ||
    search.hasta ||
    search.soloVales ||
    search.noRegularizados ||
    search.soloVencidos ||
    search.soloPorVencer;
  const masActivos = [
    search.desde,
    search.hasta,
    search.soloVales,
    search.noRegularizados,
    search.soloVencidos,
  ].filter(Boolean).length;

  return (
    <BarraFiltros
      aria-label="Filtros de salidas"
      busqueda={
        <BusquedaFolio
          aria-label="Buscar por folio"
          valor={search.q}
          onCommit={(q) => onChange({ q })}
          placeholder="Folio de la salida"
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
          <div className="flex items-center gap-2">
            <Checkbox
              id="solo-vales"
              checked={search.soloVales ?? false}
              onCheckedChange={(checked) => onChange({ soloVales: checked === true || undefined })}
            />
            <label htmlFor="solo-vales" className="text-sm">
              Solo vales urgentes
            </label>
          </div>
          <div className="flex items-center gap-2">
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
          <div className="flex items-center gap-2">
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
        </>
      }
      masFiltrosActivos={masActivos}
      chips={
        <>
          {search.rqId && (
            <FiltroActivoChip label="Requisición" onQuitar={() => onChange({ rqId: undefined })} />
          )}
          {search.soloPorVencer && (
            <FiltroActivoChip
              label="Vales por vencer"
              onQuitar={() => onChange({ soloPorVencer: undefined })}
            />
          )}
        </>
      }
      onLimpiar={
        algunFiltro
          ? () =>
              onChange({
                q: undefined,
                subAlmacenId: undefined,
                rqId: undefined,
                desde: undefined,
                hasta: undefined,
                soloVales: undefined,
                noRegularizados: undefined,
                soloVencidos: undefined,
                soloPorVencer: undefined,
              })
          : undefined
      }
      total={total != null ? `${total} ${total === 1 ? 'salida' : 'salidas'}` : undefined}
    />
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
