import { useMemo } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Bell, CheckCircle2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  DateTimeDisplay,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { useAlertasCartera, useAtenderAlerta } from '@/features/cxc/api/useAlertas';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import { TipoAlertaCartera } from '@/features/cxc/api/types';
import { ETIQUETA_TIPO_ALERTA } from '@/features/cxc/lib/glosario';
import type { AlertasSearch } from '@/features/cxc/lib/alertas-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useQueryClient } from '@tanstack/react-query';
import { esApiError, esConflictoConcurrencia } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { cxcKeys } from '@/features/cxc/api/keys';
import { cn } from '@/lib/utils';

const FROM = '/_app/cxc/alertas/' as const;
const SENTINEL_ALL = '__all__';

const OPCIONES_TIPO = [
  TipoAlertaCartera.Solunion90d,
  TipoAlertaCartera.ExcesoCredito,
  TipoAlertaCartera.AutoBloqueoVencimiento,
] as const;

/**
 * <c>Bandeja de alertas de cartera</c> (CXC-FE-PR7, P2). Alertas del
 * worker diario (SOLUNION 90d / exceso de crédito / auto-bloqueo);
 * por default muestra las pendientes. Atender exige
 * <c>cobranza.registrar</c> (es una gestión de cartera). Las
 * notificaciones push/correo siguen en
 * PLATFORM-TODO(&lt;Notificaciones&gt;).
 */
export function AlertasPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAtender = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar,
  );
  const atender = useAtenderAlerta();
  const queryClient = useQueryClient();
  const conflictDialog = useConflictDialog();

  function manejarError(error: unknown, fallback: string) {
    // 409: otra sesión ya atendió/cambió la alerta; la versión en caché quedó
    // vieja. Se refresca vía el diálogo de conflicto en vez de un toast que
    // deja reintentando con la versión obsoleta.
    if (esConflictoConcurrencia(error)) {
      conflictDialog.openSimple({
        onRefrescar: () =>
          queryClient.invalidateQueries({ queryKey: cxcKeys.alertas() }),
        traceId: error.traceId,
      });
      return;
    }
    toast.error(esApiError(error) ? error.problem.title : fallback);
  }

  const pendientes = search.pendientes ?? true;

  const query = useAlertasCartera({
    atendida: pendientes ? false : undefined,
    tipo: search.tipo,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () =>
      Array.from(
        new Set((query.data?.items ?? []).map((a) => a.clienteId)),
      ).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc(
    { ids: clienteIds },
    { enabled: clienteIds.length > 0 },
  );
  const nombres = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of lookup.data ?? []) m.set(c.id, c.razonSocial);
    return m;
  }, [lookup.data]);

  function actualizarSearch(parcial: Partial<AlertasSearch>) {
    navigate({ to: '/cxc/alertas', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((a) => {
    if (q.length === 0) return true;
    return (
      a.detalle.toLowerCase().includes(q) ||
      (nombres.get(a.clienteId) ?? '').toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-4 px-4 py-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">
          Alertas de cartera
        </h1>
        <p className="text-sm text-muted-foreground">
          Evaluación diaria: SOLUNION 90 días, exceso de crédito y
          auto-bloqueo por vencimientos.
        </p>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Mostrar</label>
          <Select
            value={pendientes ? 'pendientes' : 'todas'}
            onValueChange={(v) =>
              actualizarSearch({
                pendientes: v === 'pendientes' ? undefined : false,
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por atención" className="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="pendientes">Pendientes</SelectItem>
              <SelectItem value="todas">Todas</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Tipo</label>
          <Select
            value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                tipo:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as TipoAlertaCartera),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por tipo" className="w-64">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              {OPCIONES_TIPO.map((t) => (
                <SelectItem key={t} value={String(t)}>
                  {ETIQUETA_TIPO_ALERTA[t]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las alertas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[
            { width: 'w-40' },
            { width: 'w-48' },
            { width: 'w-full' },
            { width: 'w-28' },
            { width: 'w-20' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Bell className="h-10 w-10" />}
          title={
            q
              ? `Ninguna alerta coincide con "${search.q}".`
              : pendientes
                ? 'Sin alertas pendientes — cartera al día.'
                : 'Sin alertas con los filtros actuales.'
          }
          description="El worker evalúa la cartera una vez al día."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Tipo</th>
                <th className="px-3 py-2 font-medium">Cliente</th>
                <th className="px-3 py-2 font-medium">Detalle</th>
                <th className="px-3 py-2 font-medium">Disparada</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((a) => (
                <tr
                  key={a.id}
                  className={cn('hover:bg-muted/30', a.atendida && 'opacity-60')}
                >
                  <td className="px-3 py-2">
                    <ChipTipoAlerta tipo={a.tipo} />
                  </td>
                  <td className="max-w-56 truncate px-3 py-2">
                    {nombres.get(a.clienteId) ?? a.clienteId.slice(0, 8)}
                    <p className="font-mono text-[11px] text-muted-foreground">
                      {a.moneda}
                    </p>
                  </td>
                  <td className="max-w-96 px-3 py-2 text-xs">{a.detalle}</td>
                  <td className="px-3 py-2 text-xs text-muted-foreground">
                    <DateTimeDisplay value={a.disparadaEn} />
                    {a.atendida && a.atendidaEn && (
                      <p className="mt-0.5 flex items-center gap-1 text-emerald-700 dark:text-emerald-400">
                        <CheckCircle2 className="h-3 w-3" aria-hidden="true" />
                        Atendida
                      </p>
                    )}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {puedeAtender && !a.atendida && (
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={atender.isPending}
                        onClick={() =>
                          atender.mutate(
                            {
                              id: a.id,
                              versionEsperada: a.version,
                              // Key fresca por submit: el backend exige UUID v4 puro.
                              idempotencyKey: crypto.randomUUID(),
                            },
                            {
                              onSuccess: () => toast.success('Alerta atendida.'),
                              onError: (e) =>
                                manejarError(e, 'No se pudo atender la alerta.'),
                            },
                          )
                        }
                      >
                        Atender
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function ChipTipoAlerta({ tipo }: { tipo: TipoAlertaCartera }) {
  const clases: Record<TipoAlertaCartera, string> = {
    [TipoAlertaCartera.Solunion90d]:
      'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300',
    [TipoAlertaCartera.ExcesoCredito]:
      'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300',
    [TipoAlertaCartera.AutoBloqueoVencimiento]:
      'bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-300',
  };
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[11px] font-medium',
        clases[tipo],
      )}
    >
      {ETIQUETA_TIPO_ALERTA[tipo]}
    </span>
  );
}
