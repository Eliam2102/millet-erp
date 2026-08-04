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
  ProveedorSelector,
  SortableHeader,
  SucursalSelector,
  TableSkeleton,
  clickableRowProps,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { useFacturas } from '@/features/cxp/api/useFacturas';
import {
  EstadoPasivo,
  type FacturaListItem,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoPasivoChip } from '@/features/cxp/components/EstadoPasivoChip';
import { CapturarFacturaSheet } from '@/features/cxp/components/CapturarFacturaSheet';
import type { FacturasSearch } from '@/features/cxp/lib/facturas-search-schema';

const FROM = '/_app/cxp/facturas/' as const;
const SENTINEL_ALL = '__all__';

type SortKey =
  | 'folioProveedor'
  | 'proveedorNombre'
  | 'fechaDocumento'
  | 'fechaVencimiento'
  | 'total'
  | 'saldoPendiente'
  | 'estado';

/**
 * <c>P1 — Bandeja de Facturas de Proveedor</c> (doc 07 §FE-F2-PR1).
 * Tabla con filtros server-side (estado, proveedor, sucursal) +
 * búsqueda client-side por folio. Click en folio abre el detalle.
 *
 * <para>Botón "Capturar factura" visible para usuarios con
 * <c>cuentas_por_pagar.facturas.capturar</c>. Sin permiso queda oculto;
 * la captura sigue posible desde la bandeja de CFDIs cuando exista
 * (PLATFORM-TODO &lt;CapturaDesdeCfdi&gt;).</para>
 */
export function FacturasPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeCapturar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarFacturasCapturar,
  );
  const [capturaAbierta, setCapturaAbierta] = useState(false);

  const query = useFacturas({
    estado: search.estado,
    proveedorId: search.proveedorId,
    sucursalId: search.sucursalId,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<FacturasSearch>) {
    navigate({
      to: '/cxp/facturas',
      search: { ...search, ...parcial },
    });
  }

  const itemsFiltrados = useMemo(() => {
    const items = query.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter(
      (f) =>
        (f.folioProveedor ?? '').toLowerCase().includes(needle) ||
        (f.serieProveedor ?? '').toLowerCase().includes(needle),
    );
  }, [query.data, search.q]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Facturas</h1>
          <p className="text-sm text-muted-foreground">
            Bandeja del agregado FacturaProveedor. Captura conciliada con OC,
            revisión y autorización del ciclo del pasivo.
          </p>
        </div>
        {puedeCapturar && (
          <Button onClick={() => setCapturaAbierta(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Capturar factura
          </Button>
        )}
      </div>

      <FiltrosToolbar search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las facturas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={8}
          columns={[
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-32' },
          ]}
        />
      ) : itemsFiltrados.length === 0 ? (
        <EmptyState
          title="Sin facturas"
          description={
            search.q ||
            search.estado != null ||
            search.proveedorId ||
            search.sucursalId
              ? 'No hay facturas que coincidan con los filtros aplicados.'
              : 'Aún no hay facturas capturadas. Usa "Capturar factura" para iniciar.'
          }
        />
      ) : (
        <TablaFacturas items={itemsFiltrados} />
      )}

      <CapturarFacturaSheet
        open={capturaAbierta}
        onOpenChange={setCapturaAbierta}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: FacturasSearch;
  onChange: (parcial: Partial<FacturasSearch>) => void;
}

function FiltrosToolbar({ search, onChange }: FiltrosToolbarProps) {
  const algunFiltro =
    search.q ||
    search.estado != null ||
    search.proveedorId ||
    search.sucursalId;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Folio / serie</label>
        <Input
          aria-label="Buscar por folio o serie"
          value={search.q ?? ''}
          onChange={(e) => onChange({ q: e.target.value || undefined })}
          placeholder="Buscar…"
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
                v === SENTINEL_ALL ? undefined : (Number(v) as EstadoPasivo),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoPasivo.Capturada)}>
              Capturada
            </SelectItem>
            <SelectItem value={String(EstadoPasivo.EnRevision)}>
              En revisión
            </SelectItem>
            <SelectItem value={String(EstadoPasivo.Autorizada)}>
              Autorizada
            </SelectItem>
            <SelectItem value={String(EstadoPasivo.Pagada)}>Pagada</SelectItem>
            <SelectItem value={String(EstadoPasivo.Cancelada)}>
              Cancelada
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Proveedor</label>
        <ProveedorSelector
          value={search.proveedorId ?? null}
          onChange={(id) => onChange({ proveedorId: id ?? undefined })}
          placeholder="Todos los proveedores"
          className="w-56"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Sucursal</label>
        <SucursalSelector
          value={search.sucursalId ?? null}
          onChange={(id) => onChange({ sucursalId: id ?? undefined })}
          placeholder="Todas las sucursales"
          className="w-56"
        />
      </div>
      {algunFiltro && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
              estado: undefined,
              proveedorId: undefined,
              sucursalId: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaFacturasProps {
  items: readonly FacturaListItem[];
}

function TablaFacturas({ items }: TablaFacturasProps) {
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
                columnKey="folioProveedor"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="proveedorNombre"
                label="Proveedor"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaDocumento"
                label="Fecha doc."
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaVencimiento"
                label="Vencimiento"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">OC</th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="total"
                label="Total"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="saldoPendiente"
                label="Saldo"
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
          {ordenados.map((f) => {
            const folioLabel = f.serieProveedor
              ? `${f.serieProveedor}-${f.folioProveedor ?? ''}`
              : (f.folioProveedor ?? '—');
            const rowProps = clickableRowProps(() =>
              navigate({
                to: '/cxp/facturas/$id',
                params: { id: f.id },
              }),
            );
            return (
            <tr
              key={f.id}
              {...rowProps}
              className={`border-t hover:bg-muted/30 ${rowProps.className}`}
              aria-label={`Abrir factura ${folioLabel}`}
            >
              <td className="px-3 py-2 font-mono text-xs text-primary">
                {folioLabel}
              </td>
              <td className="max-w-56 truncate px-3 py-2">
                {f.proveedorNombre ?? (
                  <span className="font-mono text-xs text-muted-foreground">
                    {abreviar(f.proveedorId)}
                  </span>
                )}
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {formatearFecha(f.fechaDocumento)}
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {f.fechaVencimiento}
              </td>
              <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
                {f.ordenCompraId ? abreviar(f.ordenCompraId) : '—'}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(f.total, f.moneda)}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(f.saldoPendiente, f.moneda)}
              </td>
              <td className="px-3 py-2">
                <EstadoPasivoChip estado={f.estado} />
              </td>
            </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function abreviar(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
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

function formatearFecha(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('es-MX', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });
  } catch {
    return iso;
  }
}
