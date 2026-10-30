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
import { ETIQUETA_ESTADO_AUTORIZACION } from '../lib/glosario';

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
import { Ban, Plus, ShieldCheck, Unlock, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

import { useUsuarios } from '@/features/catalogos/api';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import {
  useAutorizacionesCredito,
  useBeneficiariosCredito,
  useCancelarAutorizacionCredito,
  useDecisionesLiberacion,
} from '@/features/cxc/api/useLiberaciones';
import {
  ChipEstadoAutorizacion,
  ChipResultadoLiberacion,
} from '@/features/cxc/components/ChipsLiberacion';
import { DecidirLiberacionSheet } from '@/features/cxc/components/DecidirLiberacionSheet';
import { NuevaAutorizacionDialog } from '@/features/cxc/components/NuevaAutorizacionDialog';
import { AutorizarPendienteDialog } from '@/features/cxc/components/AutorizarPendienteDialog';
import { RechazarPendienteDialog } from '@/features/cxc/components/RechazarPendienteDialog';
import { puedeAutorizarPendiente } from '@/features/cxc/lib/autorizar-pendiente';
import {
  EstadoAutorizacionCredito,
  ResultadoLiberacion,
  type DecisionLiberacionResponse,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_REGLA_LIBERACION,
  ETIQUETA_RESULTADO_LIBERACION,
  formatoCobertura,
  formatoMonto,
} from '@/features/cxc/lib/glosario';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { LiberacionesSearch } from '@/features/cxc/lib/liberaciones-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/liberaciones/' as const;

const OPCIONES_RESULTADO = [
  ResultadoLiberacion.PendienteAutorizacion,
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
  const puedeDecidir = useHasPermission(PermisosCanonicos.CuentasPorCobrarLiberacionDecidir);
  const puedeOverride = useHasPermission(PermisosCanonicos.CuentasPorCobrarLiberacionOverride);

  const [sheetDecidir, setSheetDecidir] = useState(false);
  const [dialogAutorizacion, setDialogAutorizacion] = useState(false);

  const tab = search.tab ?? 'decisiones';

  function actualizarSearch(parcial: Partial<LiberacionesSearch>) {
    navigate({ to: '/cxc/liberaciones', search: { ...search, ...parcial } });
  }

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <PageHeader
        titulo={<>Liberación de pedidos</>}
        descripcion={
          <>Decisiones con cascada serie → crédito → override y autorizaciones consumibles.</>
        }
        acciones={
          <>
            <div className="flex items-center gap-2">
              {puedeOverride && (
                <Button variant="outline" onClick={() => setDialogAutorizacion(true)}>
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
          </>
        }
      />

      <Tabs
        value={tab}
        onValueChange={(v) => actualizarSearch({ tab: v as LiberacionesSearch['tab'] })}
      >
        <TabsList>
          <TabsTrigger value="decisiones">Decisiones</TabsTrigger>
          <TabsTrigger value="autorizaciones">Autorizaciones</TabsTrigger>
        </TabsList>
        <TabsContent value="decisiones" className="mt-4">
          <TabDecisiones
            search={search}
            puedeOverride={puedeOverride}
            onFiltrarResultado={(r) => actualizarSearch({ resultado: r })}
          />
        </TabsContent>
        <TabsContent value="autorizaciones" className="mt-4">
          <TabAutorizaciones puedeOverride={puedeOverride} />
        </TabsContent>
      </Tabs>

      <DecidirLiberacionSheet open={sheetDecidir} onOpenChange={setSheetDecidir} />
      <NuevaAutorizacionDialog open={dialogAutorizacion} onOpenChange={setDialogAutorizacion} />
    </div>
  );
}

function TabDecisiones({
  search,
  puedeOverride,
  onFiltrarResultado,
}: {
  search: LiberacionesSearch;
  puedeOverride: boolean;
  onFiltrarResultado: (r: LiberacionesSearch['resultado']) => void;
}) {
  const usuarioActualId = useAuthStore((s) => s.user?.id ?? null);
  const [porAutorizar, setPorAutorizar] = useState<DecisionLiberacionResponse | null>(null);
  const [porRechazar, setPorRechazar] = useState<DecisionLiberacionResponse | null>(null);
  const query = useDecisionesLiberacion({
    resultado: search.resultado,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () => Array.from(new Set((query.data?.items ?? []).map((d) => d.clienteId))).sort(),
    [query.data],
  );
  // Puede fallar con 403 si el usuario no tiene lineas-credito.leer —
  // fallback al id corto, mismo criterio que los catálogos de Compras.
  const lookup = useClientesLookupCxc({ ids: clienteIds }, { enabled: clienteIds.length > 0 });
  const nombres = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of lookup.data ?? []) m.set(c.id, c.razonSocial);
    return m;
  }, [lookup.data]);

  // C7.3: nombre de quien evaluó. Catálogo de usuarios (identidad.usuarios.leer)
  // o, para el gerente de CxC, los usuarios con permiso de decidir
  // (/autorizaciones/beneficiarios); si ninguno responde, id corto.
  const usuarios = useUsuarios();
  const evaluadores = useBeneficiariosCredito({ enabled: puedeOverride });
  const nombreUsuario = useMemo(() => {
    const m = new Map<string, string>();
    for (const u of evaluadores.data ?? []) m.set(u.id, u.nombre);
    for (const u of usuarios.data?.items ?? []) m.set(u.id, u.nombre);
    return (id: string) => m.get(id) ?? id.slice(0, 8);
  }, [usuarios.data, evaluadores.data]);

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((d) => {
    if (q.length === 0) return true;
    return (
      d.pedidoRef.toLowerCase().includes(q) ||
      (nombres.get(d.clienteId) ?? '').toLowerCase().includes(q)
    );
  });

  const pagina = usePaginacionLocal(items, JSON.stringify(search));

  return (
    <div className="space-y-3">
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={OPCIONES_RESULTADO.map((r) => ({
              valor: r,
              label: ETIQUETA_RESULTADO_LIBERACION[r],
            }))}
            valor={search.resultado}
            conteos={undefined}
            onChange={(v) => onFiltrarResultado(v as ResultadoLiberacion | undefined)}
          />
        }
        filtros={<BarraFiltros total={`${items.length} registros en el lote`} />}
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
              { width: 'w-24' },
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
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Pedido</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Monto</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">
                    Crédito al decidir
                  </TableHead>
                  <TableHead className="px-3 py-2 font-medium">Resultado</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Regla</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Evaluó</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {pagina.visibles.map((d) => (
                  <TableRow key={d.id} className="h-12 hover:bg-surface-subtle">
                    <TableCell className="px-3 py-2 font-mono">{d.pedidoRef}</TableCell>
                    <TableCell className="max-w-64 truncate px-3 py-2">
                      {nombres.get(d.clienteId) ?? '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(d.montoPedido, d.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(d.creditoDisponibleSnapshot, d.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipResultadoLiberacion resultado={d.resultado} />
                      {formatoCobertura(d.porcentajeCobertura) && (
                        <p className="mt-0.5 text-xs text-ink-muted">
                          {formatoCobertura(d.porcentajeCobertura)}
                        </p>
                      )}
                      {d.resultado === ResultadoLiberacion.PendienteAutorizacion &&
                        d.resolucionId && (
                          <p className="mt-0.5 text-xs text-ink-muted">Ya resuelta</p>
                        )}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs">
                      {ETIQUETA_REGLA_LIBERACION[d.reglaAplicada]}
                      {d.motivo && (
                        <p className="mt-0.5 max-w-48 truncate text-ink-muted" title={d.motivo}>
                          {d.motivo}
                        </p>
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs">
                      <p>{nombreUsuario(d.decididoPor)}</p>
                      <p className="text-ink-muted">
                        <DateTimeDisplay value={d.decididoEn} />
                      </p>
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
                      {puedeAutorizarPendiente(d, puedeOverride, usuarioActualId) && (
                        <div className="flex justify-end gap-2">
                          <Button variant="outline" size="sm" onClick={() => setPorAutorizar(d)}>
                            <ShieldCheck className="mr-1 h-3.5 w-3.5" />
                            Autorizar
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            className="text-danger-fg"
                            onClick={() => setPorRechazar(d)}
                          >
                            <Ban className="mr-1 h-3.5 w-3.5" />
                            Rechazar
                          </Button>
                        </div>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
      <AutorizarPendienteDialog
        decision={porAutorizar}
        onOpenChange={(open) => {
          if (!open) setPorAutorizar(null);
        }}
      />
      <RechazarPendienteDialog
        decision={porRechazar}
        onOpenChange={(open) => {
          if (!open) setPorRechazar(null);
        }}
      />
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
    return nombreUsuario.get(id) ?? '—';
  }

  const [estado, setEstado] = useState<number | undefined>();
  const items = (query.data?.items ?? []).filter(
    (a) => estado === undefined || a.estado === estado,
  );
  const pagina = usePaginacionLocal(items, String(estado));

  return (
    <div className="space-y-3">
      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.values(EstadoAutorizacionCredito).map((e) => ({
              valor: e,
              label: ETIQUETA_ESTADO_AUTORIZACION[e],
            }))}
            valor={estado}
            conteos={undefined}
            onChange={setEstado}
          />
        }
        filtros={<BarraFiltros total={`${items.length} registros en el lote`} />}
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
        ) : items.length === 0 ? (
          <EmptyState
            icon={<Unlock className="h-10 w-10" />}
            title="Sin autorizaciones de crédito."
            description="El gerente puede crear una con 'Nueva autorización'; vence a más tardar en 24 horas."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Pedido</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Beneficiario</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Motivo</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Vigente hasta</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {pagina.visibles.map((a) => (
                  <TableRow key={a.id} className="h-12 hover:bg-surface-subtle">
                    <TableCell className="px-3 py-2">
                      <ChipEstadoAutorizacion estado={a.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {a.clienteOPedidoRef}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      {resolverUsuario(a.beneficiarioUsuarioId)}
                      <p className="text-xs text-ink-muted">
                        autorizó {resolverUsuario(a.supervisorUsuarioId)}
                      </p>
                    </TableCell>
                    <TableCell className="max-w-64 truncate px-3 py-2">{a.motivo}</TableCell>
                    <TableCell className="px-3 py-2 text-xs text-ink-muted">
                      <DateTimeDisplay value={a.vigenteHasta} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
                      {puedeOverride && a.estado === EstadoAutorizacionCredito.Autorizada && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-danger-fg"
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
                                onSuccess: () => toast.success('Autorización cancelada.'),
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
