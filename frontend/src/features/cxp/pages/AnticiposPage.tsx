import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
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
import { useAnticipos } from '@/features/cxp/api/useNotasYAnticipos';
import { EstadoAnticipo } from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoAnticipoChip } from '@/features/cxp/components/EstadoChips';
import { NuevoAnticipoSheet } from '@/features/cxp/components/NuevoAnticipoSheet';
import type { AnticiposSearch } from '@/features/cxp/lib/notas-y-anticipos-search-schema';

const FROM = '/_app/cxp/anticipos' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Bandeja de Anticipos</c> (doc 07 §FE-F4-PR1). Muestra los
 * anticipos abiertos y su saldo amortizable. La aplicación a saldo de
 * factura ocurre desde el detalle de factura (inline).
 */
export function AnticiposPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeCapturar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarAnticiposCapturar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const query = useAnticipos({
    estado: search.estado,
    proveedorId: search.proveedorId,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<AnticiposSearch>) {
    navigate({ to: '/cxp/anticipos', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Anticipos</h1>
          <p className="text-sm text-muted-foreground">
            Anticipos a proveedores (CFDI serie FANT). Se amortizan contra
            facturas finales desde el detalle de la factura.
          </p>
        </div>
        {puedeCapturar && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Capturar anticipo
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
                    : (Number(v) as EstadoAnticipo),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-44">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem value={String(EstadoAnticipo.Abierto)}>
                Abierto
              </SelectItem>
              <SelectItem value={String(EstadoAnticipo.Amortizado)}>
                Amortizado
              </SelectItem>
              <SelectItem value={String(EstadoAnticipo.Cancelado)}>
                Cancelado
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Proveedor</label>
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
          title="No se pudieron cargar los anticipos"
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
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-28' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin anticipos"
          description={
            search.estado != null || search.proveedorId
              ? 'No hay anticipos que coincidan con los filtros.'
              : 'Aún no hay anticipos capturados.'
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
                <th className="px-3 py-2 text-right">Entregado</th>
                <th className="px-3 py-2 text-right">Amortizado</th>
                <th className="px-3 py-2 text-right">Saldo</th>
                <th className="px-3 py-2 text-left">Estado</th>
              </tr>
            </thead>
            <tbody>
              {items.map((a) => (
                <tr key={a.id} className="border-t hover:bg-muted/30">
                  <td className="px-3 py-2 font-mono text-xs">
                    {abreviar(a.uuidCfdi)}
                  </td>
                  <td className="max-w-56 truncate px-3 py-2">
                    {a.proveedorNombre ?? (
                      <span className="font-mono text-xs text-muted-foreground">
                        {abreviar(a.proveedorId)}
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {a.serie}-{a.folioProveedor ?? '—'}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {formatearMonto(a.montoEntregado, a.moneda)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {formatearMonto(a.montoAmortizado, a.moneda)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono font-semibold">
                    {formatearMonto(a.saldoAmortizable, a.moneda)}
                  </td>
                  <td className="px-3 py-2">
                    <EstadoAnticipoChip estado={a.estado} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <NuevoAnticipoSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />
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
