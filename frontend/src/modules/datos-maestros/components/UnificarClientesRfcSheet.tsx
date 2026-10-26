import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { useId, useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import {
  Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle,
} from '@/components/ui/sheet';
import { esApiError } from '@/lib/api';
import { formatDate } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import {
  PERMISO_UNIFICAR_CLIENTES,
  useResolverConflictoAw,
  useRfcRepetido,
  useUnificarClientesRfc,
  type CambioMaestroAw,
  type ClienteRfcRepetido,
} from '@/modules/datos-maestros/api/sincronizacion-aw';

const MOTIVO_MIN = 5;
const MOTIVO_MAX = 300;

const fusiona = (c: ClienteRfcRepetido) => c.pedidosAw === 0 && c.pedidosErp === 0;

function mensajeError(e: unknown, defecto: string) {
  return esApiError(e) ? (e.problem.detail ?? e.problem.title) : defecto;
}

function Consecuencia({ c, principal }: { c: ClienteRfcRepetido; principal: boolean }) {
  if (c.unificadoEnClienteId != null) return <><Badge variant="neutral">Unificado</Badge><span>Ya está unificado en otro cliente.</span></>;
  if (principal) return <><Badge variant="success">Principal</Badge><span>Conserva su historial y recibe los pedidos nuevos.</span></>;
  return fusiona(c)
    ? <><Badge variant="neutral">Se fusiona</Badge><span>Se desactiva; no tiene historial.</span></>
    : <><Badge variant="info">Se agrupa</Badge><span>Conserva saldos y facturas; los pedidos nuevos van al principal.</span></>;
}

/** Sheet P4 para unificar los clientes de A+W que comparten el RFC de un conflicto `RfcCompartido`. */
export function UnificarClientesRfcSheet({ conflicto, onClose }: { conflicto: CambioMaestroAw; onClose: () => void }) {
  const rfc = conflicto.valorDespues ?? '';
  const puede = useHasPermission(PERMISO_UNIFICAR_CLIENTES);
  const q = useRfcRepetido(rfc);
  const unificar = useUnificarClientesRfc();
  const separar = useResolverConflictoAw();
  const [elegido, setElegido] = useState<string | null>(null);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const motivoId = useId();
  const ayudaId = useId();

  const grupo = q.data;
  const principalId = elegido ?? grupo?.principalSugeridoId ?? '';
  const otros = grupo?.clientes.filter((c) => c.clienteId !== principalId && c.unificadoEnClienteId == null) ?? [];
  const nFusiona = otros.filter(fusiona).length;
  const nAgrupa = otros.length - nFusiona;
  const principal = grupo?.clientes.find((c) => c.clienteId === principalId);
  const limpio = motivo.trim();
  const motivoValido = limpio.length >= MOTIVO_MIN && limpio.length <= MOTIVO_MAX;
  const editable = puede && grupo != null && !grupo.resuelto && otros.length > 0;
  const ocupado = unificar.isPending || separar.isPending;
  const sucio = limpio !== '' || (elegido != null && elegido !== grupo?.principalSugeridoId);
  const noExiste = esApiError(q.error) && q.error.status === 404;

  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(onClose);
  function cerrar() {
    if (ocupado) return;
    setDirty(sucio);
    solicitarCierre();
  }

  function confirmarUnificar() {
    setError(null);
    unificar.mutate(
      { rfc, principalClienteId: principalId, motivo: limpio },
      {
        onSuccess: () => { toast.success(`RFC ${rfc} unificado en ${principal?.razonSocial ?? 'el principal'}`); onClose(); },
        onError: (e) => setError(mensajeError(e, 'No se pudo unificar. Reintenta.')),
      },
    );
  }

  function mantenerSeparados() {
    setError(null);
    separar.mutate(
      { entidad: 'clientes', id: conflicto.id, resolucion: 'MantenerSeparados', version: conflicto.version },
      {
        onSuccess: () => { toast.success(`Clientes con RFC ${rfc} mantenidos separados`); onClose(); },
        onError: (e) => setError(mensajeError(e, 'No se pudo resolver el conflicto. Reintenta.')),
      },
    );
  }

  return (
    <Sheet open onOpenChange={(o) => { if (!o) cerrar(); }}>
      <SheetContent size="lg" side="right">
        <SheetHeader>
          <SheetTitle>{`Unificar clientes con RFC ${rfc}`}</SheetTitle>
          <SheetDescription>
            Elige el cliente principal. A+W no se modifica: la unificación solo ocurre en el ERP.
          </SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-5">
          {q.isLoading && (
            <div aria-busy="true" aria-label="Cargando clientes con este RFC" className="space-y-3">
              <Skeleton className="h-20 w-full" />
              <Skeleton className="h-20 w-full" />
              <Skeleton className="h-24 w-full" />
            </div>
          )}
          {noExiste && <p role="status" className="py-3 text-ink-muted">Este RFC ya no tiene clientes repetidos.</p>}
          {q.isError && !noExiste && (
            <div role="alert" className="flex items-center gap-3 text-danger-fg">
              <span>No se pudieron cargar los clientes de este RFC.</span>
              <Button size="sm" variant="secondary" onClick={() => void q.refetch()}>Reintentar</Button>
            </div>
          )}
          {grupo && (
            <>
              {!puede && (
                <p role="note" className="rounded-md bg-warning-note-bg p-3 text-warning-note-fg">
                  Necesitas el permiso de unificar clientes para resolver este conflicto. Aquí solo puedes consultarlo.
                </p>
              )}
              {grupo.resuelto && (
                <p role="status" className="rounded-md bg-success-bg p-3 text-success-fg">
                  Este RFC ya está unificado en {principal?.razonSocial ?? 'el cliente principal'}.
                </p>
              )}
              <fieldset className="space-y-2">
                <legend className="mb-1 text-sm font-medium">Cliente principal</legend>
                <p className="mb-2 text-xs text-ink-muted">Sugerido: {grupo.motivoSugerencia}</p>
                {grupo.clientes.map((c) => {
                  const esPrincipal = c.clienteId === principalId;
                  return (
                    <label
                      key={c.clienteId}
                      className="flex cursor-pointer gap-3 rounded-md border border-line p-3 has-[:checked]:border-brand has-[:checked]:bg-surface-subtle has-[:disabled]:cursor-default has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-brand"
                    >
                      <input
                        type="radio"
                        name={`rfc-principal-${rfc}`}
                        className="mt-1 h-4 w-4 accent-primary"
                        checked={esPrincipal}
                        disabled={!editable || c.unificadoEnClienteId != null}
                        onChange={() => setElegido(c.clienteId)}
                      />
                      <span className="min-w-0 flex-1 space-y-1">
                        <span className="flex flex-wrap items-baseline gap-x-2">
                          <span className="break-words font-medium">{c.razonSocial}</span>
                          <span className="font-mono text-xs text-ink-muted">{c.referenciaAw ? (/^AW-/i.test(c.referenciaAw) ? c.referenciaAw : `AW-${c.referenciaAw}`) : '—'}</span>
                        </span>
                        <span className="block text-xs text-ink-muted">
                          {`Pedidos A+W: ${c.pedidosAw}`}
                          {c.ultimoPedidoAw && ` (último ${formatDate(c.ultimoPedidoAw)})`}
                          {` · Pedidos ERP: ${c.pedidosErp}`}
                        </span>
                        <span className="flex flex-wrap items-center gap-2 text-xs">
                          <Consecuencia c={c} principal={esPrincipal} />
                        </span>
                      </span>
                    </label>
                  );
                })}
              </fieldset>

              {editable && (
                <>
                  <div role="note" className="rounded-md bg-surface-subtle p-3 text-sm">
                    <p>
                      {`Todo se unifica en ${principal?.razonSocial ?? ''}.`}
                      {nFusiona > 0 && ` ${nFusiona} se fusiona${nFusiona > 1 ? 'n' : ''} y queda${nFusiona > 1 ? 'n' : ''} inactivo${nFusiona > 1 ? 's' : ''}.`}
                      {nAgrupa > 0 && ` ${nAgrupa} se agrupa${nAgrupa > 1 ? 'n' : ''}: conserva${nAgrupa > 1 ? 'n' : ''} saldos y facturas.`}
                    </p>
                    <p className="text-xs text-ink-muted">A+W no se modifica. Los pedidos nuevos se facturan al principal.</p>
                  </div>
                  <div className="space-y-1">
                    <label htmlFor={motivoId} className="text-sm font-medium">Motivo</label>
                    <Textarea
                      id={motivoId}
                      value={motivo}
                      maxLength={MOTIVO_MAX}
                      aria-describedby={ayudaId}
                      onChange={(e) => setMotivo(e.target.value)}
                    />
                    <p id={ayudaId} className="text-xs text-ink-muted">{`${MOTIVO_MIN} a ${MOTIVO_MAX} caracteres · ${limpio.length}/${MOTIVO_MAX}`}</p>
                  </div>
                </>
              )}
            </>
          )}
          {error && <p role="alert" className="text-sm text-danger-fg">{error}</p>}
        </SheetBody>
        <SheetFooter>
          {editable ? (
            <>
              <Button type="button" variant="secondary" disabled={ocupado} onClick={mantenerSeparados}>
                {separar.isPending ? 'Guardando…' : 'Mantener separados'}
              </Button>
              <Button type="button" disabled={!motivoValido || ocupado} aria-busy={unificar.isPending} onClick={confirmarUnificar}>
                {unificar.isPending ? 'Unificando…' : 'Unificar'}
              </Button>
            </>
          ) : (
            <Button type="button" variant="secondary" onClick={cerrar}>Cerrar</Button>
          )}
        </SheetFooter>
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}
