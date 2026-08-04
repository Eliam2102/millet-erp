import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Ban, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
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
  TableSkeleton,
  UsuarioSelector,
} from '@/components/erp';
import {
  useAprobadoresLimites,
  useCerrarAprobadorLimite,
} from '@/features/cxp/api/useAdminCxp';
import {
  TipoGastoAprobador,
  TipoGastoAprobadorLabels,
  type AprobadorLimite,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { NuevoAprobadorSheet } from '@/features/cxp/components/NuevoAprobadorSheet';
import type { AprobadoresLimitesSearch } from '@/features/cxp/lib/admin-search-schema';

const FROM = '/_app/cxp/admin/aprobadores' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Catálogo de Aprobadores con Límite</c> (cxp-fe/admin-pages).
 * Bandeja con acción inline "Cerrar vigencia". Defaults a soloVigentes
 * para que aparezcan solo los vigentes; toggle para ver históricos.
 */
export function AprobadoresLimitesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdmin = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const soloVigentes = search.soloVigentes ?? true;
  const query = useAprobadoresLimites({
    empleadoId: search.empleadoId,
    tipoGasto: search.tipoGasto,
    soloVigentes,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<AprobadoresLimitesSearch>) {
    navigate({
      to: '/cxp/admin/aprobadores',
      search: { ...search, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Aprobadores con límite
          </h1>
          <p className="text-sm text-muted-foreground">
            Catálogo de aprobadores con monto máximo por tipo de gasto.
            Backend valida los topes al autorizar comprobaciones/viáticos.
          </p>
        </div>
        {puedeAdmin && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo aprobador
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Empleado</label>
          <UsuarioSelector
            value={search.empleadoId ?? null}
            onChange={(id) => actualizarSearch({ empleadoId: id ?? undefined })}
            placeholder="Todos los empleados"
            className="w-56"
          />
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Tipo gasto</label>
          <Select
            value={
              search.tipoGasto != null ? String(search.tipoGasto) : SENTINEL_ALL
            }
            onValueChange={(v) =>
              actualizarSearch({
                tipoGasto:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as TipoGastoAprobador),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por tipo de gasto" className="w-48">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem
                value={String(TipoGastoAprobador.ReembolsoCajaChica)}
              >
                Caja chica
              </SelectItem>
              <SelectItem value={String(TipoGastoAprobador.Viaticos)}>
                Viáticos
              </SelectItem>
              <SelectItem
                value={String(TipoGastoAprobador.TarjetaCreditoEmpresarial)}
              >
                TC empresarial
              </SelectItem>
              <SelectItem value={String(TipoGastoAprobador.OtrosSinOc)}>
                Otros sin OC
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="flex items-center gap-2 pt-5">
          <Checkbox
            id="soloVigentes"
            checked={soloVigentes}
            onCheckedChange={(v) =>
              actualizarSearch({ soloVigentes: v === true })
            }
          />
          <Label htmlFor="soloVigentes" className="text-sm">
            Solo vigentes
          </Label>
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los aprobadores"
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
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin aprobadores"
          description="No hay aprobadores con los filtros aplicados. Crea el primero."
        />
      ) : (
        <Tabla items={items} puedeAdmin={puedeAdmin} />
      )}

      <NuevoAprobadorSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />
    </div>
  );
}

function Tabla({
  items,
  puedeAdmin,
}: {
  items: readonly AprobadorLimite[];
  puedeAdmin: boolean;
}) {
  const cerrar = useCerrarAprobadorLimite();
  const hoy = hoyLocalISO();

  function handleCerrar(a: AprobadorLimite) {
    if (
      !window.confirm(
        `¿Cerrar la vigencia del aprobador (empleado ${a.empleadoId.slice(0, 8)}…) hoy ${hoy}?`,
      )
    )
      return;
    cerrar
      .mutateAsync({ id: a.id, versionEsperada: a.version, fecha: hoy })
      .then(
        () => toast.success('Vigencia cerrada'),
        (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al cerrar.');
        },
      );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Empleado</th>
            <th className="px-3 py-2 text-left">Tipo gasto</th>
            <th className="px-3 py-2 text-right">Monto máx.</th>
            <th className="px-3 py-2 text-left">Vigencia</th>
            <th className="px-3 py-2 text-right" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {items.map((a) => (
            <tr key={a.id} className="border-t hover:bg-muted/30">
              <td className="px-3 py-2 font-mono text-xs">
                {a.empleadoId.slice(0, 8)}…{a.empleadoId.slice(-4)}
              </td>
              <td className="px-3 py-2">
                {TipoGastoAprobadorLabels[a.tipoGasto]}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {fmt(a.montoMax, a.moneda)}
              </td>
              <td className="px-3 py-2 text-xs">
                {a.vigenciaDesde} → {a.vigenciaHasta ?? '(abierta)'}
              </td>
              <td className="px-3 py-2 text-right">
                {puedeAdmin && a.vigenciaHasta == null && (
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-destructive"
                    onClick={() => handleCerrar(a)}
                    disabled={cerrar.isPending}
                  >
                    <Ban className="mr-1 h-3 w-3" />
                    Cerrar
                  </Button>
                )}
              </td>
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
