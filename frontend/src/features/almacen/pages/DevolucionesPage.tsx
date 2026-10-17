import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { ChevronDown, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
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
  ProveedorSelector,
  SortableHeader,
  TableSkeleton,
  clickableRowProps,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { useDevolucionesProveedor } from '@/features/almacen/api/useDevolucionesProveedor';
import {
  EstadoDevolucionProveedor,
  EstadoDevolucionProveedorLabels,
  type DevolucionProveedorListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { NuevaDevolucionProveedorSheet } from '@/features/almacen/components/NuevaDevolucionProveedorSheet';
import { AplicarDevolucionInternaSheet } from '@/features/almacen/components/AplicarDevolucionInternaSheet';
import {
  MatRevSheet,
  type MatRevModo,
} from '@/features/almacen/components/MatRevSheet';
import type { DevolucionesSearch } from '@/features/almacen/lib/devoluciones-search-schema';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { Field } from '@/features/almacen/components/internal/Field';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

const FROM = '/_app/almacen/devoluciones/' as const;
const SENTINEL_ALL = '__all__';

type SortKey =
  | 'proveedorId'
  | 'estado'
  | 'montoTotalMxn'
  | 'solicitadaAt';

/**
 * <c>P5 — Bandeja de devoluciones</c> (doc 07 §FE-F4-PR1).
 *
 * <para>Muestra principalmente devoluciones a proveedor (8.B, que sí
 * tienen state machine). Las devoluciones internas (8.A) y MAT-REV
 * son one-shot — se acceden via el dropdown "Otras acciones" y sus
 * movimientos resultantes aparecen en la bandeja de salidas/recepciones
 * según el tipo.</para>
 */
export function DevolucionesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const puedeIniciarProveedor = useHasPermission(
    PermisosCanonicos.AlmacenDevolucionesProveedorIniciar,
  );
  const puedeCapturarInterna = useHasPermission(
    PermisosCanonicos.AlmacenDevolucionesInternasCapturar,
  );

  const [sheetProveedorAbierto, setSheetProveedorAbierto] = useState(false);
  const [sheetInternaAbierto, setSheetInternaAbierto] = useState(false);
  const [matRevSheet, setMatRevSheet] = useState<MatRevModo | null>(null);

  const query = useDevolucionesProveedor({
    estado: search.estado,
    proveedorId: search.proveedorId,
    soloPendientesNcFiscal: search.soloPendientesNcFiscal,
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  function actualizarSearch(parcial: Partial<DevolucionesSearch>) {
    navigate({
      to: '/almacen/devoluciones',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
  }

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Devoluciones a proveedor"
        descripcion={
          <>
            Sub-flujo 8.B con estado de Borrador → En autorización → Autorizada →
            Registrada → Conciliada NC fiscal. Devoluciones internas y MAT-REV
            en el menú "Otras acciones".
          </>
        }
        acciones={
          <>
            <div className="flex items-center gap-2">
              {puedeIniciarProveedor && (
                <Button onClick={() => setSheetProveedorAbierto(true)}>
                  <Plus className="mr-2 h-4 w-4" />
                  Nueva devolución a proveedor
                </Button>
              )}
              {puedeCapturarInterna && (
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="outline">
                      Otras acciones
                      <ChevronDown className="ml-2 h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onClick={() => setSheetInternaAbierto(true)}>
                      Devolución interna (8.A)
                    </DropdownMenuItem>
                    <DropdownMenuItem onClick={() => setMatRevSheet('baja')}>
                      MAT-REV — Baja por daño
                    </DropdownMenuItem>
                    <DropdownMenuItem onClick={() => setMatRevSheet('reincorporar')}>
                      MAT-REV — Reincorporar
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              )}
            </div>
          </>
        }
      />

      <FiltrosToolbar search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las devoluciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-40' },
            { width: 'w-40' },
          ]}
        />
      ) : (query.data?.items ?? []).length === 0 ? (
        <EmptyState
          title="Sin devoluciones a proveedor"
          description={
            search.q ||
            search.estado != null ||
            search.proveedorId ||
            search.soloPendientesNcFiscal
              ? 'No hay devoluciones que coincidan con los filtros.'
              : 'Aún no se ha iniciado ninguna devolución a proveedor.'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TablaDevoluciones items={query.data!.items} />
          <PaginacionTabla
            total={query.data!.total}
            offset={query.data!.offset}
            limit={query.data!.limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
      )}

      <NuevaDevolucionProveedorSheet
        open={sheetProveedorAbierto}
        onOpenChange={setSheetProveedorAbierto}
      />
      <AplicarDevolucionInternaSheet
        open={sheetInternaAbierto}
        onOpenChange={setSheetInternaAbierto}
      />
      <MatRevSheet
        open={matRevSheet != null}
        onOpenChange={(v) => {
          if (!v) setMatRevSheet(null);
        }}
        modo={matRevSheet ?? 'baja'}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: DevolucionesSearch;
  onChange: (parcial: Partial<DevolucionesSearch>) => void;
}

function FiltrosToolbar({ search, onChange }: FiltrosToolbarProps) {
  const algun =
    search.q ||
    search.estado != null ||
    search.proveedorId ||
    search.soloPendientesNcFiscal;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <Field label="Folio de salida" className="gap-1">
        <BusquedaFolio
          aria-label="Buscar por folio de salida"
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
                  : (Number(v) as EstadoDevolucionProveedor),
            })
          }
        >
          <SelectTrigger {...c} className="w-52">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {(
              [
                EstadoDevolucionProveedor.Borrador,
                EstadoDevolucionProveedor.EnAutorizacion,
                EstadoDevolucionProveedor.Autorizada,
                EstadoDevolucionProveedor.Registrada,
                EstadoDevolucionProveedor.ConciliadaConNcFiscal,
                EstadoDevolucionProveedor.Rechazada,
              ] as const
            ).map((e) => (
              <SelectItem key={e} value={String(e)}>
                {EstadoDevolucionProveedorLabels[e]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        )}
      </Field>
      <Field label="Proveedor" className="gap-1">
        <ProveedorSelector
          value={search.proveedorId ?? null}
          onChange={(id) => onChange({ proveedorId: id ?? undefined })}
          placeholder="Todos"
          className="w-72"
        />
      </Field>
      <div className="flex items-center gap-2 pb-1.5">
        <Checkbox
          id="solo-pendientes-nc"
          checked={search.soloPendientesNcFiscal ?? false}
          onCheckedChange={(checked) =>
            onChange({
              soloPendientesNcFiscal: checked === true || undefined,
            })
          }
        />
        <label htmlFor="solo-pendientes-nc" className="text-sm">
          Solo pendientes de NC fiscal
        </label>
      </div>
      {algun && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
              estado: undefined,
              proveedorId: undefined,
              soloPendientesNcFiscal: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

function TablaDevoluciones({
  items,
}: {
  items: readonly DevolucionProveedorListItem[];
}) {
  const navigate = useNavigate();
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const ordenados = useMemo(() => {
    if (sort == null) return items;
    return [...items].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [items, sort]);

  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>
              <SortableHeader
                columnKey="proveedorId"
                label="Proveedor"
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
                columnKey="solicitadaAt"
                label="Solicitada"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>Folio salida</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {ordenados.map((d) => {
            const rowProps = clickableRowProps(() =>
              navigate({
                to: '/almacen/devoluciones/proveedor/$id',
                params: { id: d.id },
              }),
            );
            return (
              <TableRow
                key={d.id}
                {...rowProps}
                className={`hover:bg-surface-subtle ${rowProps.className}`}
              >
                <TableCell>
                  <Link
                    to="/almacen/devoluciones/proveedor/$id"
                    params={{ id: d.id }}
                    onClick={(e) => e.stopPropagation()}
                    className="text-brand hover:underline"
                    aria-label={`Abrir devolución a proveedor ${d.proveedorNombre ?? truncarId(d.proveedorId)}`}
                  >
                    {d.proveedorNombre ?? (
                      <span className="font-mono text-xs" title={d.proveedorId}>
                        {truncarId(d.proveedorId)}
                      </span>
                    )}
                  </Link>
                </TableCell>
                <TableCell>
                  <EstadoAlmacenBadge tipo="devolucion-proveedor" estado={d.estado} />
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={d.montoTotalMxn} currency="MXN" strong />
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  <DateTimeDisplay value={d.solicitadaAt} variant="corta" />
                </TableCell>
                <TableCell className="font-mono text-xs">
                  {d.folioMovimientoSalida ?? (
                    <span className="italic text-ink-muted">—</span>
                  )}
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
