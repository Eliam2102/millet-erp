import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
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
  ProveedorSelector,
  SortableHeader,
  SucursalSelector,
  TableSkeleton,
  clickableRowProps,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { ProveedorCelda } from '@/features/cxp/components/ProveedorCelda';
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
import { hoyLocalISO } from '@/lib/datetime';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

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
 * Tabla paginada con filtros y búsqueda `q` server-side (folio, serie o
 * UUID del CFDI). Click en el folio abre el detalle.
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
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  function actualizarSearch(parcial: Partial<FacturasSearch>) {
    navigate({
      to: '/cxp/facturas',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Facturas"
        descripcion="Bandeja del agregado FacturaProveedor. Captura conciliada con OC, revisión y autorización del ciclo del pasivo."
        acciones={
          <>
            {puedeCapturar && (
              <Button onClick={() => setCapturaAbierta(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Capturar factura
              </Button>
            )}
          </>
        }
      />

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
      ) : items.length === 0 ? (
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
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TablaFacturas items={items} />
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
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
      <BusquedaFolio
        aria-label="Buscar por folio, serie o UUID"
        valor={search.q}
        onCommit={(q) => onChange({ q })}
        placeholder="Folio, serie o UUID"
        className="w-64"
      />
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
  const search = useSearch({ from: FROM });
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
                columnKey="folioProveedor"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="proveedorNombre"
                label="Proveedor"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="fechaDocumento"
                label="Fecha doc."
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="fechaVencimiento"
                label="Vencimiento"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>OC</TableHead>
            <TableHead className="text-right">
              <SortableHeader
                columnKey="total"
                label="Total"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="text-right">
              <SortableHeader
                columnKey="saldoPendiente"
                label="Saldo"
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
              <TableRow
                key={f.id}
                {...rowProps}
                className={`hover:bg-surface-subtle ${rowProps.className}`}
              >
                <TableCell className="font-mono text-xs">
                  <Link
                    to="/cxp/facturas/$id"
                    params={{ id: f.id }}
                    search={search}
                    onClick={(e) => e.stopPropagation()}
                    className="text-brand hover:underline"
                    aria-label={`Abrir factura ${folioLabel}`}
                  >
                    {folioLabel}
                  </Link>
                </TableCell>
                <TableCell>
                  <ProveedorCelda nombre={f.proveedorNombre} rfc={f.proveedorRfc} />
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  <DateTimeDisplay value={f.fechaDocumento} variant="corta" />
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  <DateTimeDisplay
                    value={f.fechaVencimiento}
                    variant="corta"
                    vencida={
                      f.fechaVencimiento < hoyLocalISO() &&
                      f.estado !== EstadoPasivo.Pagada &&
                      f.estado !== EstadoPasivo.Cancelada
                    }
                  />
                </TableCell>
                <TableCell className="font-mono text-xs text-ink-muted">
                  {f.ordenCompraId ? (f.ordenCompraFolio?.trim() || '[FOLIO POR CONFIRMAR]') : '—'}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={f.total} currency={f.moneda} strong />
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={f.saldoPendiente} currency={f.moneda} strong />
                </TableCell>
                <TableCell>
                  <EstadoPasivoChip estado={f.estado} />
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
