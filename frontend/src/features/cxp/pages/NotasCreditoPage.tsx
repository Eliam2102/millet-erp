import { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Eye, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
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
  TableSkeleton,
} from '@/components/erp';
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
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<NotasCreditoSearch>) {
    navigate({ to: '/cxp/notas-credito', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Notas de crédito
          </h1>
          <p className="text-sm text-muted-foreground">
            NC recibidas del proveedor (relación CFDI 01/03/07). Las NC en
            "En espera" se vincularán automáticamente cuando entre la factura
            origen.
          </p>
        </div>
        {puedeCapturar && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Capturar NC
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
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
        {(search.estado != null || search.proveedorId) && (
          <Button
            variant="ghost"
            onClick={() =>
              actualizarSearch({ estado: undefined, proveedorId: undefined })
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
            { width: 'w-32' },
            { width: 'w-28' },
            { width: 'w-32' },
            { width: 'w-28' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin notas de crédito"
          description={
            search.estado != null || search.proveedorId
              ? 'No hay NCs que coincidan con los filtros.'
              : 'Aún no se han recibido NCs. Captura desde el botón superior.'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">UUID</th>
                <th className="px-3 py-2 text-left">Proveedor</th>
                <th className="px-3 py-2 text-left">Folio</th>
                <th className="px-3 py-2 text-left">Tipo</th>
                <th className="px-3 py-2 text-right">Total</th>
                <th className="px-3 py-2 text-right">Saldo</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-right" aria-label="Acciones" />
              </tr>
            </thead>
            <tbody>
              {items.map((n) => (
                <tr
                  key={n.id}
                  className="border-t hover:bg-muted/30"
                >
                  <td className="px-3 py-2 font-mono text-xs">
                    {abreviar(n.uuidCfdi)}
                  </td>
                  <td className="max-w-56 truncate px-3 py-2">
                    {n.proveedorNombre ?? (
                      <span className="font-mono text-xs text-muted-foreground">
                        {abreviar(n.proveedorId)}
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {n.serieProveedor
                      ? `${n.serieProveedor}-${n.folioProveedor ?? ''}`
                      : (n.folioProveedor ?? '—')}
                  </td>
                  <td className="px-3 py-2 text-xs">
                    {(TipoNotaCreditoLabels as Record<number, string>)[n.tipo] ?? `Tipo ${n.tipo}`}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {formatearMonto(n.total, n.moneda)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {formatearMonto(n.saldoPorAplicar, n.moneda)}
                  </td>
                  <td className="px-3 py-2">
                    <EstadoNotaCreditoChip estado={n.estado} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Button asChild variant="ghost" size="sm">
                      <Link
                        to="/cxp/notas-credito/$id"
                        params={{ id: n.id }}
                        aria-label={`Ver detalle de NC ${abreviar(n.uuidCfdi)}`}
                      >
                        <Eye className="mr-1 h-3 w-3" />
                        Ver detalle
                      </Link>
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
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
