import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, TabsEstado, PaginacionTabla } from '@/components/erp';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { opcionesEstado } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { toast } from 'sonner';
import { Inbox } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useDepositos,
  useDepositosSinIdentificar,
  useRechazarDeposito,
} from '@/features/tesoreria/api/useTesoreria';
import { DepositosSinIdentificar } from '@/features/cxc/components/DepositosSinIdentificar';
import { useNuevaPropuesta } from '@/features/cxc/components/nueva-propuesta-context';
import {
  EstadoDepositoConfirmacion,
  type DepositoConfirmacionResponse,
} from '@/features/tesoreria/api/types';
import { EstadoDepositoBadge, ReppTimbradoBadge } from '@/features/tesoreria/components/Badges';
import { ConfirmarDepositoDialog } from '@/features/tesoreria/components/ConfirmarDepositoDialog';
import { useAuthStore } from '@/lib/auth/auth-store';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { formatDate } from '@/lib/datetime';
import { origenDeposito } from '@/features/tesoreria/lib/depositos';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, codigoSoporte } from '@/lib/api';

const FILTRO_TODOS = 'todos';
const MENSAJE_PROPIA = 'No puedes confirmar una propuesta que tú hiciste';
/**
 * C7.4: valor centinela de la pestaña «Sin identificar» dentro de TabsEstado
 * (numérico; no es un estado de confirmación). Sin ruta nueva.
 */
const TAB_SIN_IDENTIFICAR = 0;

/**
 * <c>Depósitos pendientes</c> (TES-FE-PR4b, P2 / §3.3 TES-9): CxC
 * propone el matching depósito ↔ facturas y Caja publica su expectativa;
 * Tesorería confirma aquí el hecho BANCARIO (RN-6) o rechaza con motivo
 * para que CxC re-proponga [T-G7]. Confirmar dispara la emisión del REPP
 * en Facturación; el timbrado es un momento fiscal aparte (badge REPP).
 */
export function BandejaDepositos() {
  const puedeConfirmar = useHasPermission(PermisosCanonicos.TesoreriaDepositosConfirmar);
  const puedeRechazar = useHasPermission(PermisosCanonicos.TesoreriaDepositosRechazar);
  // C7.4: Tesorería ve los ingresos sin identificar con su permiso de lectura de
  // movimientos; «Identificar» (proponer) es de CxC.
  const puedeVerSinIdentificar = useHasPermission(PermisosCanonicos.TesoreriaMovimientosVer);
  const puedeProponer = useHasPermission(PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer);
  const nuevaPropuesta = useNuevaPropuesta();
  // R12: quien propuso el cobro en CxC no lo confirma (el backend responde DEP_MISMO_USUARIO).
  const usuarioId = useAuthStore((st) => st.user?.id ?? null);

  const [estado, setEstado] = useState<string>(String(EstadoDepositoConfirmacion.Pendiente));
  const [origen, setOrigen] = useState<string>(FILTRO_TODOS);

  const vistaSinIdentificar = estado === String(TAB_SIN_IDENTIFICAR);
  const sinIdentificar = useDepositosSinIdentificar({ enabled: vistaSinIdentificar && puedeVerSinIdentificar });
  const query = useDepositos({
    estado: estado === FILTRO_TODOS || vistaSinIdentificar ? undefined : Number(estado),
    soloPropuestas: origen === FILTRO_TODOS ? undefined : origen === 'propuestas',
    limit: 200,
  });

  const rechazar = useRechazarDeposito();

  const [confirmando, setConfirmando] = useState<DepositoConfirmacionResponse | null>(null);
  const [rechazando, setRechazando] = useState<DepositoConfirmacionResponse | null>(null);

  const items = query.data?.items ?? [];

  function onRechazar(motivo: string) {
    if (rechazando == null) return;
    rechazar.mutate(
      {
        depositoId: rechazando.id,
        motivo,
        versionEsperada: rechazando.version,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Propuesta rechazada', {
            description: 'CxC recibirá el motivo y podrá re-proponer.',
          });
          setRechazando(null);
        },
        onError: (error) => {
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo rechazar', {
            description: esApiError(error)
              ? (error.problem.detail ?? codigoSoporte(error))
              : undefined,
          });
        },
      },
    );
  }

  const paginacion = usePaginacionLocal(items, `${estado}:${origen}`);

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Depósitos pendientes</>}
        descripcion={
          <>
            Propuestas de CxC y expectativas de Caja. Confirmar registra el hecho bancario y dispara
            la emisión del REPP en Facturación — el timbrado fiscal se refleja aparte en el badge
            REPP.
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              ...opcionesEstado('deposito'),
              ...(puedeVerSinIdentificar ? [{ valor: TAB_SIN_IDENTIFICAR, label: 'Sin identificar' }] : []),
            ]}
            valor={estado === FILTRO_TODOS ? undefined : Number(estado)}
            onChange={(v) => setEstado(v == null ? FILTRO_TODOS : String(v))}
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <>
                <div className="flex items-center gap-2">
                  <Select value={origen} onValueChange={setOrigen}>
                    <SelectTrigger className="w-40" aria-label="Origen">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={FILTRO_TODOS}>Todos los orígenes</SelectItem>
                      <SelectItem value="propuestas">Propuestas CxC</SelectItem>
                      <SelectItem value="caja">Expectativas de Caja</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </>
            }
          />
        }
        pie={
          vistaSinIdentificar ? undefined : (
            <PaginacionTabla
              total={items.length}
              offset={paginacion.offset}
              limit={10}
              tamanos={[]}
              onChange={paginacion.onChange}
            />
          )
        }
      >
        {vistaSinIdentificar ? (
          <DepositosSinIdentificar
            query={sinIdentificar}
            q=""
            onIdentificar={puedeProponer ? (d) => nuevaPropuesta.abrir(d) : undefined}
          />
        ) : query.isError ? (
          <ErrorState
            title="No se pudieron cargar los depósitos"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-28' },
              { width: 'w-56' },
              { width: 'w-32' },
              { width: 'w-24' },
              { width: 'w-24' },
              { width: 'w-28' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<Inbox className="h-10 w-10" />}
            title="Sin depósitos en este filtro."
            description="Las propuestas aparecen cuando CxC hace el matching depósito ↔ facturas; las expectativas, al cerrar sesiones de caja con efectivo."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Origen</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Referencia</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Monto</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Facturas</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Recibido</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Fiscal</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {paginacion.visibles.map((d) => (
                  <TableRow key={d.id} className="hover:bg-surface-muted/30">
                    <TableCell className="px-3 py-2 text-xs">{origenDeposito(d)}</TableCell>
                    <TableCell className="max-w-64 px-3 py-2">
                      <p className="truncate">
                        {d.clienteRazonSocial ?? (d.cajaSesionId != null ? 'Corte de caja' : '—')}
                      </p>
                      {d.cajaSesionId != null && d.cajeroNombre ? (
                        <p className="truncate text-xs text-ink-muted">Cajero: {d.cajeroNombre}</p>
                      ) : (
                        <p className="font-mono text-xs text-ink-muted">{d.clienteClave ?? ''}</p>
                      )}
                    </TableCell>
                    <TableCell
                      className="max-w-44 truncate px-3 py-2 font-mono text-xs"
                      title={d.depositoRef ?? undefined}
                    >
                      {d.depositoRef ?? '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {d.montoEsperado != null
                        ? formatoMonto(d.montoEsperado, d.moneda ?? 'MXN')
                        : '—'}
                      {d.saldoPorDepositar != null && d.estado === EstadoDepositoConfirmacion.Parcial && (
                        <p className="text-xs text-ink-muted">
                          Saldo {formatoMonto(d.saldoPorDepositar, d.moneda ?? 'MXN')} · {d.fichas?.length ?? 0} ficha(s)
                        </p>
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      {d.propuestaCxcId != null ? d.facturas.length : '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs">
                      {/* recibidoEn es DateTimeOffset (instante), no DateOnly:
                        formatoFecha haría split('-') y saldría basura. */}
                      {formatDate(d.recibidoEn)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoDepositoBadge estado={d.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      {d.estado === EstadoDepositoConfirmacion.Confirmada &&
                      d.propuestaCxcId != null ? (
                        <ReppTimbradoBadge timbrado={d.reppTimbrado} />
                      ) : d.estado === EstadoDepositoConfirmacion.Rechazada &&
                        d.motivoRechazo != null ? (
                        <span
                          className="block max-w-40 truncate text-xs text-ink-muted"
                          title={d.motivoRechazo}
                        >
                          {d.motivoRechazo}
                        </span>
                      ) : (
                        <span className="text-xs text-ink-muted">—</span>
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
                      {(d.estado === EstadoDepositoConfirmacion.Pendiente ||
                        (d.estado === EstadoDepositoConfirmacion.Parcial && d.cajaSesionId != null)) && (
                        <div className="flex justify-end gap-2">
                          {puedeRechazar && d.estado === EstadoDepositoConfirmacion.Pendiente && (
                            <Button variant="outline" size="sm" onClick={() => setRechazando(d)}>
                              Rechazar
                            </Button>
                          )}
                          {puedeConfirmar &&
                            (usuarioId != null && d.propuestoPor === usuarioId ? (
                              <span title={MENSAJE_PROPIA} className="inline-flex">
                                <Button size="sm" disabled aria-describedby={`propia-${d.id}`}>
                                  Confirmar
                                </Button>
                                <span id={`propia-${d.id}`} className="sr-only">
                                  {MENSAJE_PROPIA}
                                </span>
                              </span>
                            ) : (
                              <Button size="sm" onClick={() => setConfirmando(d)}>
                                {d.cajaSesionId != null ? 'Agregar ficha' : 'Confirmar'}
                              </Button>
                            ))}
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

      {query.data != null && query.data.total > (query.data.items?.length ?? 0) && (
        <p className="text-xs text-ink-muted">
          Cargados {query.data.items.length} de {query.data.total} depósitos.
        </p>
      )}

      <ConfirmarDepositoDialog
        deposito={confirmando}
        onOpenChange={(o) => {
          if (!o) setConfirmando(null);
        }}
      />
      <MotivoDialog
        open={rechazando != null}
        onOpenChange={(o) => {
          if (!o) setRechazando(null);
        }}
        titulo="Rechazar propuesta de depósito"
        descripcion="El depósito no aparece en banco o el monto no coincide. CxC recibirá el motivo y podrá re-proponer."
        confirmLabel="Rechazar"
        onConfirm={onRechazar}
        pending={rechazar.isPending}
      />
    </div>
  );
}
