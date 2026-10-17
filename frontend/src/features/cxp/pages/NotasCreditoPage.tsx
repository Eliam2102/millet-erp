import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
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
  TableSkeleton,
} from '@/components/erp';
import { BusquedaFolio } from '@/features/almacen/components/internal/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/features/almacen/lib/paginacion';
import { ProveedorCelda } from '@/features/cxp/components/ProveedorCelda';
import { useNotasCredito } from '@/features/cxp/api/useNotasYAnticipos';
import {
  EstadoNotaCredito,
  TipoNotaCreditoLabels,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoNotaCreditoChip } from '@/features/cxp/components/EstadoChips';
import { NuevaNotaCreditoSheet } from '@/features/cxp/components/NuevaNotaCreditoSheet';
import type { NotasCreditoSearch } from '@/features/cxp/lib/notas-y-anticipos-search-schema';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

const FROM = '/_app/cxp/notas-credito/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Bandeja de Notas de Crédito</c> (doc 07 §FE-F4-PR1). Tabla con
 * filtros por estado + proveedor. Indicador especial para NCs en
 * <c>EnEspera</c> (sin match a factura).
 */
export function NotasCreditoPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeCapturar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const query = useNotasCredito({
    estado: search.estado,
    proveedorId: search.proveedorId,
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  function actualizarSearch(parcial: Partial<NotasCreditoSearch>) {
    navigate({
      to: '/cxp/notas-credito',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Notas de crédito"
        descripcion={
          <>
            NC recibidas del proveedor (relación CFDI 01/03/07). Las NC en
            "En espera" se vincularán automáticamente cuando entre la factura
            origen.
          </>
        }
        acciones={
          <>
            {puedeCapturar && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Capturar NC
              </Button>
            )}
          </>
        }
      />

      <div className="flex flex-wrap items-end gap-3">
        <BusquedaFolio
          aria-label="Buscar por folio, serie o UUID"
          valor={search.q}
          onCommit={(q) => actualizarSearch({ q })}
          placeholder="Folio, serie o UUID"
          className="w-64"
        />
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Estado</label>
          <Select
            value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                estado:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as EstadoNotaCredito),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-48">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem value={String(EstadoNotaCredito.EnEspera)}>
                En espera
              </SelectItem>
              <SelectItem value={String(EstadoNotaCredito.Abierta)}>
                Abierta
              </SelectItem>
              <SelectItem value={String(EstadoNotaCredito.Aplicada)}>
                Aplicada
              </SelectItem>
              <SelectItem value={String(EstadoNotaCredito.Cancelada)}>
                Cancelada
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">
            Proveedor
          </label>
          <ProveedorSelector
            value={search.proveedorId ?? null}
            onChange={(id) =>
              actualizarSearch({ proveedorId: id ?? undefined })
            }
            placeholder="Todos los proveedores"
            className="w-56"
          />
        </div>
        {(search.estado != null || search.proveedorId || search.q) && (
          <Button
            variant="ghost"
            onClick={() =>
              actualizarSearch({ estado: undefined, proveedorId: undefined, q: undefined })
            }
          >
            Limpiar
          </Button>
        )}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las NCs"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-40' },
            { width: 'w-28' },
            { width: 'w-32' },
            { width: 'w-28' },
            { width: 'w-28' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin notas de crédito"
          description={
            search.estado != null || search.proveedorId || search.q
              ? 'No hay NCs que coincidan con los filtros.'
              : 'Aún no se han recibido NCs. Captura desde el botón superior.'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Folio</TableHead>
                  <TableHead>UUID</TableHead>
                  <TableHead>Proveedor</TableHead>
                  <TableHead>Tipo</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                  <TableHead className="text-right">Saldo</TableHead>
                  <TableHead>Estado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((n) => {
                  const folio = n.serieProveedor
                    ? `${n.serieProveedor}-${n.folioProveedor ?? ''}`
                    : (n.folioProveedor ?? abreviar(n.uuidCfdi));
                  return (
                    <TableRow key={n.id} className="hover:bg-surface-subtle">
                      <TableCell className="font-mono text-xs">
                        <Link
                          to="/cxp/notas-credito/$id"
                          params={{ id: n.id }}
                          className="text-brand hover:underline"
                          aria-label={`Abrir nota de crédito ${folio}`}
                        >
                          {folio}
                        </Link>
                      </TableCell>
                      <TableCell className="font-mono text-xs text-ink-muted" title={n.uuidCfdi}>
                        {abreviar(n.uuidCfdi)}
                      </TableCell>
                      <TableCell>
                        <ProveedorCelda nombre={n.proveedorNombre} rfc={n.proveedorRfc} />
                      </TableCell>
                      <TableCell className="text-xs">
                        {(TipoNotaCreditoLabels as Record<number, string>)[n.tipo] ?? `Tipo ${n.tipo}`}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        <MoneyDisplay amount={n.total} currency={n.moneda} strong />
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        <MoneyDisplay amount={n.saldoPorAplicar} currency={n.moneda} strong />
                      </TableCell>
                      <TableCell>
                        <EstadoNotaCreditoChip estado={n.estado} />
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
      )}

      <NuevaNotaCreditoSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
      />
    </div>
  );
}

function abreviar(uuid: string): string {
  return uuid.length > 12 ? `${uuid.slice(0, 8)}…${uuid.slice(-4)}` : uuid;
}

