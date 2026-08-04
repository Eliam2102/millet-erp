import { useState } from 'react';
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
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { usePoliticasViaticos } from '@/features/cxp/api/useAdminCxp';
import {
  TipoDestinoViatico,
  TipoDestinoViaticoLabels,
  type PoliticaViaticos,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { NuevaPoliticaViaticosSheet } from '@/features/cxp/components/NuevaPoliticaViaticosSheet';
import type { PoliticasViaticosSearch } from '@/features/cxp/lib/admin-search-schema';

const FROM = '/_app/cxp/admin/politicas-viaticos' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Catálogo de Políticas de Viáticos</c> (cxp-fe/admin-pages).
 * Bandeja de topes por puesto + destino. El backend valida contra
 * estas políticas al solicitar viáticos.
 */
export function PoliticasViaticosPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdmin = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCatalogosPoliticasAdministrar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const query = usePoliticasViaticos({
    puestoId: search.puestoId,
    tipoDestino: search.tipoDestino,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<PoliticasViaticosSearch>) {
    navigate({
      to: '/cxp/admin/politicas-viaticos',
      search: { ...search, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Políticas de viáticos
          </h1>
          <p className="text-sm text-muted-foreground">
            Tabuladores por puesto y destino (Nacional/Internacional).
            El backend valida solicitudes de viáticos contra estos topes.
          </p>
        </div>
        {puedeAdmin && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva política
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">
            Puesto (UUID)
          </label>
          <Input
            value={search.puestoId ?? ''}
            onChange={(e) =>
              actualizarSearch({ puestoId: e.target.value || undefined })
            }
            placeholder="00000000-…"
            className="w-56 font-mono text-xs"
          />
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Destino</label>
          <Select
            value={
              search.tipoDestino != null
                ? String(search.tipoDestino)
                : SENTINEL_ALL
            }
            onValueChange={(v) =>
              actualizarSearch({
                tipoDestino:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as TipoDestinoViatico),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por destino" className="w-44">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem value={String(TipoDestinoViatico.Nacional)}>
                Nacional
              </SelectItem>
              <SelectItem value={String(TipoDestinoViatico.Internacional)}>
                Internacional
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las políticas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-24' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin políticas"
          description="No hay políticas con los filtros aplicados. Crea la primera."
        />
      ) : (
        <Tabla items={items} />
      )}

      <NuevaPoliticaViaticosSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
      />
    </div>
  );
}

function Tabla({ items }: { items: readonly PoliticaViaticos[] }) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Puesto</th>
            <th className="px-3 py-2 text-left">Destino</th>
            <th className="px-3 py-2 text-right">Monto/día</th>
            <th className="px-3 py-2 text-right">Días máx.</th>
          </tr>
        </thead>
        <tbody>
          {items.map((p) => (
            <tr key={p.id} className="border-t hover:bg-muted/30">
              <td className="px-3 py-2 font-mono text-xs">
                {p.puestoId.slice(0, 8)}…{p.puestoId.slice(-4)}
              </td>
              <td className="px-3 py-2">
                {TipoDestinoViaticoLabels[p.tipoDestino]}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {fmt(p.montoMaxDia, p.moneda)}
              </td>
              <td className="px-3 py-2 text-right">{p.diasMax}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function fmt(v: number, moneda: string): string {
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
