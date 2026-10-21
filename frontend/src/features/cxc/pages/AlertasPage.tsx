import {
  ListadoModulo,
  TabsEstado,
  BarraFiltros,
  PaginacionTabla,
  PageHeader,
  DateTimeDisplay,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';

import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { EstadoCxcBadge } from '../components/EstadoCxcBadge';

import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Bell } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

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
  const puedeAtender = useHasPermission(PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar);
  const atender = useAtenderAlerta();
  const queryClient = useQueryClient();
  const conflictDialog = useConflictDialog();

  function manejarError(error: unknown, fallback: string) {
    // 409: otra sesión ya atendió/cambió la alerta; la versión en caché quedó
    // vieja. Se refresca vía el diálogo de conflicto en vez de un toast que
    // deja reintentando con la versión obsoleta.
    if (esConflictoConcurrencia(error)) {
      conflictDialog.openSimple({
        onRefrescar: () => queryClient.invalidateQueries({ queryKey: cxcKeys.alertas() }),
        traceId: error.traceId,
      });
      return;
    }
    toast.error(esApiError(error) ? error.problem.title : fallback);
  }

  const pendientes = search.pendientes ?? true;
  const [soloAtendidas, setSoloAtendidas] = useState(false);

  const query = useAlertasCartera({
    atendida: pendientes ? false : undefined,
    tipo: search.tipo,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () => Array.from(new Set((query.data?.items ?? []).map((a) => a.clienteId))).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc({ ids: clienteIds }, { enabled: clienteIds.length > 0 });
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
    if (!pendientes && soloAtendidas && !a.atendida) return false;
    if (q.length === 0) return true;
    return (
      a.detalle.toLowerCase().includes(q) ||
      (nombres.get(a.clienteId) ?? '').toLowerCase().includes(q)
    );
  });

  const pagina = usePaginacionLocal(items, JSON.stringify([search, soloAtendidas]));

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <div>
        <PageHeader
          titulo={<>Alertas de cartera</>}
          descripcion={
            <>
              Evaluación diaria: SOLUNION 90 días, exceso de crédito y auto-bloqueo por
              vencimientos.
            </>
          }
        />
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 1, label: 'Pendientes' },
              { valor: 2, label: 'Atendidas' },
            ]}
            valor={pendientes ? 1 : soloAtendidas ? 2 : undefined}
            conteos={undefined}
            onChange={(v) => {
              setSoloAtendidas(v === 2);
              actualizarSearch({ pendientes: v === 1 ? undefined : false });
            }}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <div className="flex flex-wrap items-end gap-3">
                <div className="space-y-1">
                  <label className="text-xs text-ink-muted">Tipo</label>
                  <Select
                    value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
                    onValueChange={(v) =>
                      actualizarSearch({
                        tipo: v === SENTINEL_ALL ? undefined : (Number(v) as TipoAlertaCartera),
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
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.cambiar}
          />
        }
      >
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
            description="La cartera se evalúa una vez al día."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Tipo</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Detalle</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Disparada</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {pagina.visibles.map((a) => (
                  <TableRow
                    key={a.id}
                    className={cn('hover:bg-surface-muted/30', a.atendida && 'opacity-60')}
                  >
                    <TableCell className="px-3 py-2">
                      <ChipTipoAlerta tipo={a.tipo} />
                    </TableCell>
                    <TableCell className="max-w-56 truncate px-3 py-2">
                      {nombres.get(a.clienteId) ?? a.clienteId.slice(0, 8)}
                      <p className="font-mono text-2xs text-ink-muted">{a.moneda}</p>
                    </TableCell>
                    <TableCell className="max-w-96 px-3 py-2 text-xs">{a.detalle}</TableCell>
                    <TableCell className="px-3 py-2 text-xs text-ink-muted">
                      <DateTimeDisplay value={a.disparadaEn} />
                      {a.atendida && a.atendidaEn && (
                        <EstadoCxcBadge entidad="atencion" estado="Atendida" />
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
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
                                onError: (e) => manejarError(e, 'No se pudo atender la alerta.'),
                              },
                            )
                          }
                        >
                          Atender
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}

function ChipTipoAlerta({ tipo }: { tipo: TipoAlertaCartera }) {
  return <EstadoCxcBadge entidad="alerta" estado={tipo} />;
}
