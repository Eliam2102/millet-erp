import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { ChevronDown, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
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
import { cn } from '@/lib/utils';

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
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<DevolucionesSearch>) {
    navigate({
      to: '/almacen/devoluciones',
      search: { ...search, ...parcial },
    });
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Devoluciones a proveedor
          </h1>
          <p className="text-sm text-muted-foreground">
            Sub-flujo 8.B con estado de Borrador → En autorización → Autorizada →
            Registrada → Conciliada NC fiscal. Devoluciones internas y MAT-REV
            en el menú "Otras acciones".
          </p>
        </div>
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
      </div>

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
            search.estado != null ||
            search.proveedorId ||
            search.soloPendientesNcFiscal
              ? 'No hay devoluciones que coincidan con los filtros.'
              : 'Aún no se ha iniciado ninguna devolución a proveedor.'
          }
        />
      ) : (
        <TablaDevoluciones items={query.data!.items} />
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
    search.estado != null ||
    search.proveedorId ||
    search.soloPendientesNcFiscal;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
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
          <SelectTrigger className="w-52">
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
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Proveedor</label>
        <ProveedorSelector
          value={search.proveedorId ?? null}
          onChange={(id) => onChange({ proveedorId: id ?? undefined })}
          placeholder="Todos"
          className="w-72"
        />
      </div>
      <div className="flex items-center gap-2 pb-1">
        <input
          id="solo-pendientes-nc"
          type="checkbox"
          checked={search.soloPendientesNcFiscal ?? false}
          onChange={(e) =>
            onChange({
              soloPendientesNcFiscal: e.target.checked || undefined,
            })
          }
          className="h-4 w-4"
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
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="proveedorId"
                label="Proveedor"
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
                columnKey="solicitadaAt"
                label="Solicitada"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">Folio salida</th>
          </tr>
        </thead>
        <tbody>
          {ordenados.map((d) => {
            const rowProps = clickableRowProps(() =>
              navigate({
                to: '/almacen/devoluciones/proveedor/$id',
                params: { id: d.id },
              }),
            );
            return (
            <tr
              key={d.id}
              {...rowProps}
              className={`border-t hover:bg-muted/30 ${rowProps.className}`}
              aria-label={`Abrir devolución a proveedor ${d.proveedorNombre ?? truncarId(d.proveedorId)}`}
            >
              <td className="px-3 py-2 text-primary">
                {d.proveedorNombre ?? (
                  <span className="font-mono text-xs" title={d.proveedorId}>
                    {truncarId(d.proveedorId)}
                  </span>
                )}
              </td>
              <td className="px-3 py-2">
                <EstadoBadge estado={d.estado} />
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(d.montoTotalMxn)}
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {new Date(d.solicitadaAt).toLocaleString('es-MX')}
              </td>
              <td className="px-3 py-2 font-mono text-xs">
                {d.folioMovimientoSalida ?? (
                  <span className="italic text-muted-foreground">—</span>
                )}
              </td>
            </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function EstadoBadge({ estado }: { estado: EstadoDevolucionProveedor }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoDevolucionProveedor.Borrador &&
          'bg-slate-200 text-slate-700',
        estado === EstadoDevolucionProveedor.EnAutorizacion &&
          'bg-amber-100 text-amber-800',
        estado === EstadoDevolucionProveedor.Autorizada &&
          'bg-blue-100 text-blue-800',
        estado === EstadoDevolucionProveedor.Registrada &&
          'bg-violet-100 text-violet-800',
        estado === EstadoDevolucionProveedor.ConciliadaConNcFiscal &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoDevolucionProveedor.Rechazada &&
          'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoDevolucionProveedorLabels[estado]}
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

function truncarId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
