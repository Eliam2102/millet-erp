import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Plus, Unlock, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  DateTimeDisplay,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { useUsuarios } from '@/features/catalogos/api';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import {
  useAutorizacionesCredito,
  useCancelarAutorizacionCredito,
  useDecisionesLiberacion,
} from '@/features/cxc/api/useLiberaciones';
import {
  ChipEstadoAutorizacion,
  ChipResultadoLiberacion,
} from '@/features/cxc/components/ChipsLiberacion';
import { DecidirLiberacionSheet } from '@/features/cxc/components/DecidirLiberacionSheet';
import { NuevaAutorizacionDialog } from '@/features/cxc/components/NuevaAutorizacionDialog';
import {
  EstadoAutorizacionCredito,
  ResultadoLiberacion,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_REGLA_LIBERACION,
  ETIQUETA_RESULTADO_LIBERACION,
  formatoMonto,
} from '@/features/cxc/lib/glosario';
import type { LiberacionesSearch } from '@/features/cxc/lib/liberaciones-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/liberaciones/' as const;
const SENTINEL_ALL = '__all__';

const OPCIONES_RESULTADO = [
  ResultadoLiberacion.Liberado,
  ResultadoLiberacion.Retenido,
  ResultadoLiberacion.LiberadoConOverride,
] as const;

/**
 * <c>Bandeja de liberaciones</c> (CXC-FE-PR3, P2). Dos pestañas:
 * decisiones (inmutables, con snapshot de crédito y regla aplicada) y
 * autorizaciones consumibles (§4.3). "Decidir liberación" abre el panel
 * de acción; "Nueva autorización" el dialog del gerente.
 */
export function BandejaLiberaciones() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeDecidir = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarLiberacionDecidir,
  );
  const puedeOverride = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarLiberacionOverride,
  );

  const [sheetDecidir, setSheetDecidir] = useState(false);
  const [dialogAutorizacion, setDialogAutorizacion] = useState(false);

  const tab = search.tab ?? 'decisiones';

  function actualizarSearch(parcial: Partial<LiberacionesSearch>) {
    navigate({ to: '/cxc/liberaciones', search: { ...search, ...parcial } });
  }

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Liberación de pedidos
          </h1>
          <p className="text-sm text-muted-foreground">
            Decisiones con cascada serie → crédito → override y
            autorizaciones consumibles.
          </p>
        </div>
        <div className="flex items-center gap-2">
          {puedeOverride && (
            <Button
              variant="outline"
              onClick={() => setDialogAutorizacion(true)}
            >
              <Plus className="mr-2 h-4 w-4" />
              Nueva autorización
            </Button>
          )}
          {puedeDecidir && (
            <Button onClick={() => setSheetDecidir(true)}>
              <Unlock className="mr-2 h-4 w-4" />
              Decidir liberación
            </Button>
          )}
        </div>
      </div>

      <Tabs
        value={tab}
        onValueChange={(v) =>
          actualizarSearch({ tab: v as LiberacionesSearch['tab'] })
        }
      >
        <TabsList>
          <TabsTrigger value="decisiones">Decisiones</TabsTrigger>
          <TabsTrigger value="autorizaciones">Autorizaciones</TabsTrigger>
        </TabsList>
        <TabsContent value="decisiones" className="mt-4">
          <TabDecisiones
            search={search}
            onFiltrarResultado={(r) => actualizarSearch({ resultado: r })}
          />
        </TabsContent>
        <TabsContent value="autorizaciones" className="mt-4">
          <TabAutorizaciones puedeOverride={puedeOverride} />
        </TabsContent>
      </Tabs>

      <DecidirLiberacionSheet
        open={sheetDecidir}
        onOpenChange={setSheetDecidir}
      />
      <NuevaAutorizacionDialog
        open={dialogAutorizacion}
        onOpenChange={setDialogAutorizacion}
      />
    </div>
  );
}

function TabDecisiones({
  search,
  onFiltrarResultado,
}: {
  search: LiberacionesSearch;
  onFiltrarResultado: (r: LiberacionesSearch['resultado']) => void;
}) {
  const query = useDecisionesLiberacion({
    resultado: search.resultado,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () =>
      Array.from(
        new Set((query.data?.items ?? []).map((d) => d.clienteId)),
      ).sort(),
    [query.data],
  );
  // Puede fallar con 403 si el usuario no tiene lineas-credito.leer —
  // fallback al id corto, mismo criterio que los catálogos de Compras.
  const lookup = useClientesLookupCxc(
    { ids: clienteIds },
    { enabled: clienteIds.length > 0 },
  );
  const nombres = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of lookup.data ?? []) m.set(c.id, c.razonSocial);
    return m;
  }, [lookup.data]);

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((d) => {
    if (q.length === 0) return true;
    return (
      d.pedidoRef.toLowerCase().includes(q) ||
      (nombres.get(d.clienteId) ?? '').toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Resultado</label>
          <Select
            value={
              search.resultado != null ? String(search.resultado) : SENTINEL_ALL
            }
            onValueChange={(v) =>
              onFiltrarResultado(
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as ResultadoLiberacion),
              )
            }
          >
            <SelectTrigger aria-label="Filtrar por resultado" className="w-56">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              {OPCIONES_RESULTADO.map((r) => (
                <SelectItem key={r} value={String(r)}>
                  {ETIQUETA_RESULTADO_LIBERACION[r]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las decisiones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-32' },
            { width: 'w-48' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Unlock className="h-10 w-10" />}
          title={
            q
              ? `Ninguna decisión coincide con "${search.q}".`
              : 'Sin decisiones de liberación con los filtros actuales.'
          }
          description='Registra la primera con "Decidir liberación".'
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Pedido</th>
                <th className="px-3 py-2 font-medium">Cliente</th>
                <th className="px-3 py-2 text-right font-medium">Monto</th>
                <th className="px-3 py-2 text-right font-medium">
                  Crédito al decidir
                </th>
                <th className="px-3 py-2 font-medium">Resultado</th>
                <th className="px-3 py-2 font-medium">Regla</th>
                <th className="px-3 py-2 font-medium">Decidido</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((d) => (
                <tr key={d.id} className="hover:bg-muted/30">
                  <td className="px-3 py-2 font-mono">{d.pedidoRef}</td>
                  <td className="max-w-64 truncate px-3 py-2">
                    {nombres.get(d.clienteId) ?? d.clienteId.slice(0, 8)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatoMonto(d.montoPedido, d.moneda)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatoMonto(d.creditoDisponibleSnapshot, d.moneda)}
                  </td>
                  <td className="px-3 py-2">
                    <ChipResultadoLiberacion resultado={d.resultado} />
                  </td>
                  <td className="px-3 py-2 text-xs">
                    {ETIQUETA_REGLA_LIBERACION[d.reglaAplicada]}
                  </td>
                  <td className="px-3 py-2 text-xs text-muted-foreground">
                    <DateTimeDisplay value={d.decididoEn} />
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

function TabAutorizaciones({ puedeOverride }: { puedeOverride: boolean }) {
  const query = useAutorizacionesCredito({ limit: 200 });
  const usuarios = useUsuarios();
  const cancelar = useCancelarAutorizacionCredito();

  const nombreUsuario = useMemo(() => {
    const m = new Map<string, string>();
    for (const u of usuarios.data?.items ?? []) m.set(u.id, u.nombre);
    return m;
  }, [usuarios.data]);

  function resolverUsuario(id: string) {
    return nombreUsuario.get(id) ?? id.slice(0, 8);
  }

  return (
    <div className="space-y-3">
      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las autorizaciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[
            { width: 'w-24' },
            { width: 'w-40' },
            { width: 'w-40' },
            { width: 'w-48' },
            { width: 'w-32' },
            { width: 'w-16' },
          ]}
        />
      ) : (query.data?.items.length ?? 0) === 0 ? (
        <EmptyState
          icon={<Unlock className="h-10 w-10" />}
          title="Sin autorizaciones de crédito."
          description="El gerente puede crear una con 'Nueva autorización'; vence a más tardar en 24 horas."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Estado</th>
                <th className="px-3 py-2 font-medium">Cliente / pedido</th>
                <th className="px-3 py-2 font-medium">Beneficiario</th>
                <th className="px-3 py-2 font-medium">Motivo</th>
                <th className="px-3 py-2 font-medium">Vigente hasta</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {query.data!.items.map((a) => (
                <tr key={a.id} className="hover:bg-muted/30">
                  <td className="px-3 py-2">
                    <ChipEstadoAutorizacion estado={a.estado} />
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {a.clienteOPedidoRef}
                  </td>
                  <td className="px-3 py-2">
                    {resolverUsuario(a.beneficiarioUsuarioId)}
                    <p className="text-xs text-muted-foreground">
                      autorizó {resolverUsuario(a.supervisorUsuarioId)}
                    </p>
                  </td>
                  <td className="max-w-64 truncate px-3 py-2">{a.motivo}</td>
                  <td className="px-3 py-2 text-xs text-muted-foreground">
                    <DateTimeDisplay value={a.vigenteHasta} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    {puedeOverride &&
                      a.estado === EstadoAutorizacionCredito.Autorizada && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-destructive"
                          disabled={cancelar.isPending}
                          onClick={() =>
                            cancelar.mutate(
                              {
                                id: a.id,
                                versionEsperada: a.version,
                                // Key fresca por submit: el backend exige UUID v4 puro.
                                idempotencyKey: crypto.randomUUID(),
                              },
                              {
                                onSuccess: () =>
                                  toast.success('Autorización cancelada.'),
                                onError: (e) =>
                                  toast.error(
                                    esApiError(e)
                                      ? e.problem.title
                                      : 'No se pudo cancelar la autorización.',
                                  ),
                              },
                            )
                          }
                        >
                          <XCircle className="mr-1 h-3.5 w-3.5" />
                          Cancelar
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
